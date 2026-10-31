namespace ApiGestion.Tests;

using System.Reflection;
using ApiGestion.Controllers;
using ApiGestion.Services;
using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.SqlClient;
using ServiceLibrary;

// HU-063: un pago registrado en el Administrativo queda bien guardado en la base compartida y el Portal Deportivo lee el
// estado nuevo. A diferencia del resto de las pruebas de integración, estas corren el cobro REAL (PagosService con
// PaymentTransactionDao, que hace commit) y después leen con conexiones nuevas, como lo haría el otro sistema. Por eso
// escriben datos de verdad: solo corren contra una base descartable (una copia de prueba, con "Descartable" en el nombre).
public sealed class CobroRealFactAttribute : FactAttribute
{
    public const string Variable = "CACC_TEST_DB_DESCARTABLE";

    public CobroRealFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Prueba de punta a punta que escribe datos: definí {Variable} con la conexión a una COPIA descartable de la base "
                + "(el nombre de la base tiene que contener \"Descartable\").";
        }
    }
}

[Collection(BaseDeDatosCollection.Nombre)]
public class PagosBaseCompartidaIntegrationTests
{
    // Cuotas de 2025: ya vencidas cualquier día que se corra la prueba, así el estado de deuda las cuenta.
    private const int Anio = 2025;
    private const decimal Cuota = 20_000m;

    private static readonly Lazy<string> ConexionVerificada = new(() =>
    {
        string conexion = Environment.GetEnvironmentVariable(CobroRealFactAttribute.Variable)!;
        using var connection = new SqlConnection(conexion);
        connection.Open();
        using var cmd = new SqlCommand("SELECT DB_NAME()", connection);
        string baseActual = (string)cmd.ExecuteScalar();
        // Barrera contra apuntar por error a una base que se usa: estas pruebas graban jugadores, cuotas y pagos.
        if (!baseActual.Contains("Descartable", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"La base '{baseActual}' no parece descartable. Estas pruebas graban datos: usá una copia con \"Descartable\" en el nombre.");
        }
        return conexion;
    });

    private static string Conexion => ConexionVerificada.Value;

    private static PagosService Servicio() => new(new PagosDao(Conexion), new PaymentTransactionDao(Conexion));

    // ===== Preparación (se graba de verdad: es lo que ya estaría en la base antes del cobro) =====

    private sealed record JugadorDePrueba(int Id, string Dni, string Apellido, Dictionary<int, int> CuotaPorMes);

    // Un jugador nuevo con una cuota pendiente de $20.000 por cada mes indicado de 2025.
    private static JugadorDePrueba CrearJugadorConCuotas(params int[] meses)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        string dni = "96" + Random.Shared.Next(100000, 999999);
        string apellido = "Hu063" + Random.Shared.Next(100000, 999999);
        int categoria;
        using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction))
            categoria = Convert.ToInt32(cmd.ExecuteScalar());

        var player = new Player
        {
            FirstName = "Prueba", LastName = apellido, Dni = dni, BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino", CategoryId = categoria, JoinDate = new DateTime(Anio, 1, 1)
        };
        int jugador = PlayerDAO.InsertPlayer(connection, transaction, PlayerDAO.InsertPerson(connection, transaction, player), player);

        var cuotas = new Dictionary<int, int>();
        foreach (int mes in meses)
        {
            // OUTPUT INTO (no OUTPUT a secas) porque PAGOS tiene el trigger de auditoría.
            using var cmd = new SqlCommand(@"
                DECLARE @nueva TABLE (id INT);
                INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto)
                OUTPUT INSERTED.PK_id_pago INTO @nueva (id) VALUES (@j, @m, @m, @p, 0, 'Cuota');
                SELECT id FROM @nueva;", connection, transaction);
            cmd.Parameters.AddWithValue("@j", jugador);
            cmd.Parameters.AddWithValue("@m", Cuota);
            cmd.Parameters.AddWithValue("@p", new DateTime(Anio, mes, 1));
            cuotas[mes] = Convert.ToInt32(cmd.ExecuteScalar());
        }

        transaction.Commit();
        return new JugadorDePrueba(jugador, dni, apellido, cuotas);
    }

    // Beca o descuento en porcentaje para los meses indicados (desde el primero hasta el último día del último).
    private static int AsignarBeca(int jugador, decimal porcentaje, int desdeMes, int hastaMes)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var cmd = new SqlCommand(@"
            DECLARE @nueva TABLE (id INT);
            INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, tipo_valor, porcentaje, fecha_inicio, fecha_fin)
            OUTPUT INSERTED.PK_id_jugador_descuento INTO @nueva (id)
            VALUES (@j, (SELECT TOP (1) PK_id_descuento FROM TIPOS_DESCUENTO ORDER BY PK_id_descuento), '%', @p, @desde, @hasta);
            SELECT id FROM @nueva;", connection);
        cmd.Parameters.AddWithValue("@j", jugador);
        cmd.Parameters.AddWithValue("@p", porcentaje);
        cmd.Parameters.AddWithValue("@desde", new DateTime(Anio, desdeMes, 1));
        cmd.Parameters.AddWithValue("@hasta", new DateTime(Anio, hastaMes, 1).AddMonths(1).AddDays(-1));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // El operador que cobra: un usuario con acceso al Portal Administrativo.
    private static (int Id, string Email) Operador()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var cmd = new SqlCommand(
            "SELECT TOP (1) PK_id_usuario, email FROM USUARIOS WHERE rol_portal IN (1, 2) AND activo = 1 ORDER BY PK_id_usuario", connection);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read(), "La base de prueba no tiene un usuario del Portal Administrativo.");
        return (reader.GetInt32(0), reader.GetString(1));
    }

    // ===== Lectura posterior, con una conexión nueva (como la de otro sistema) =====

    private sealed record FilaPago(int Id, bool Estado, decimal MontoFinal, string? MetodoPago, DateTime? FechaPago,
        DateTime? FechaHoraRegistro, int? IdUsuarioRegistro, int? IdDescuento);

    private static List<FilaPago> FilasDelJugador(int jugador)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var cmd = new SqlCommand(@"
            SELECT PK_id_pago, estado, monto_final, metodo_pago, fecha_pago, fecha_hora_registro, FK_id_usuario_registro, FK_id_jugador_descuento
            FROM PAGOS WHERE FK_id_jugador = @j ORDER BY PK_id_pago", connection);
        cmd.Parameters.AddWithValue("@j", jugador);
        using var reader = cmd.ExecuteReader();
        var filas = new List<FilaPago>();
        while (reader.Read())
        {
            filas.Add(new FilaPago(
                reader.GetInt32(0), reader.GetBoolean(1), reader.GetDecimal(2),
                reader.IsDBNull(3) ? null : reader.GetString(3).Trim(),
                reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7)));
        }
        return filas;
    }

    private sealed record EventoAuditoria(string Accion, int? IdUsuario, string? Email, string? DatosDespues);

    private static List<EventoAuditoria> AuditoriaDelPago(int idPago)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var cmd = new SqlCommand(@"
            SELECT accion, id_usuario, email_usuario, datos_despues FROM AUDITORIA_CAMBIOS
            WHERE entidad = N'PAGOS' AND id_entidad = @id ORDER BY PK_id_evento", connection);
        cmd.Parameters.AddWithValue("@id", idPago.ToString());
        using var reader = cmd.ExecuteReader();
        var eventos = new List<EventoAuditoria>();
        while (reader.Read())
        {
            eventos.Add(new EventoAuditoria(reader.GetString(0).Trim(), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return eventos;
    }

    // Lo que lee el Portal Deportivo: GET api/integracion/v1/estado-deuda/dni/{dni} usa exactamente este DAO, sin caché.
    private static EstadoDeudaJugador EstadoQueLeeElDeportivo(string dni) =>
        Assert.Single(new EstadoDeudaDao(Conexion).ObtenerPorDni(new[] { dni }, null));

    // ===== Casos de la HU =====

    // Pago completo (HU-025): cobro desde la ficha. La cuota queda abonada con método, fecha y operador.
    [CobroRealFact]
    public void PagoCompleto_ElCobroDejaLaCuotaAbonadaConMetodoFechaYUsuario()
    {
        var jugador = CrearJugadorConCuotas(3, 4);
        var operador = Operador();

        CobrarPagosPendientesResultado resultado;
        using (SqlAuditActorContext.Push(operador.Id))
        {
            resultado = Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = new List<int> { jugador.CuotaPorMes[3] }, MetodoPago = "transferencia", IdUsuarioRegistro = operador.Id
            });
        }

        Assert.Equal(Cuota, resultado.MontoTotal);
        Assert.Equal("Transferencia", resultado.MetodoPago); // se guarda con su nombre canónico

        var filas = FilasDelJugador(jugador.Id);
        var marzo = Assert.Single(filas, f => f.Id == jugador.CuotaPorMes[3]);
        Assert.True(marzo.Estado);
        Assert.Equal(Cuota, marzo.MontoFinal);
        Assert.Equal("Transferencia", marzo.MetodoPago);
        Assert.Equal(RelojNegocio.Hoy, marzo.FechaPago);
        Assert.NotNull(marzo.FechaHoraRegistro);
        Assert.Equal(operador.Id, marzo.IdUsuarioRegistro);

        // Abril no se tocó.
        var abril = Assert.Single(filas, f => f.Id == jugador.CuotaPorMes[4]);
        Assert.False(abril.Estado);
        Assert.Null(abril.MetodoPago);
    }

    // Pago completo (HU-015 / HU-016): registro de un pago por el total en Efectivo. Queda el abono con método, fecha y
    // operador, y la cuota pendiente desaparece.
    [CobroRealFact]
    public void PagoCompleto_ElRegistroPorElTotalDejaElAbonoYCierraLaCuota()
    {
        var jugador = CrearJugadorConCuotas(5);
        var operador = Operador();

        RegistrarPagoResultado resultado;
        using (SqlAuditActorContext.Push(operador.Id))
        {
            resultado = Servicio().RegistrarPago(new RegistrarPagoRequest
            {
                IdJugador = jugador.Id, Periodo = "Mayo", Anio = Anio, Monto = Cuota, MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
            });
        }

        var abono = Assert.Single(FilasDelJugador(jugador.Id));
        Assert.Equal(resultado.IdPago, abono.Id);
        Assert.NotEqual(jugador.CuotaPorMes[5], abono.Id); // la fila pendiente se borró
        Assert.True(abono.Estado);
        Assert.Equal(Cuota, abono.MontoFinal);
        Assert.Equal("Efectivo", abono.MetodoPago);
        Assert.Equal(RelojNegocio.Hoy, abono.FechaPago);
        Assert.NotNull(abono.FechaHoraRegistro);
        Assert.Equal(operador.Id, abono.IdUsuarioRegistro);
    }

    // Varias cuotas en una sola operación: todas quedan abonadas con la misma fecha y hora de registro.
    [CobroRealFact]
    public void VariasCuotas_TodasQuedanAbonadasEnLaMismaOperacion()
    {
        var jugador = CrearJugadorConCuotas(6, 7, 8);
        var operador = Operador();

        using (SqlAuditActorContext.Push(operador.Id))
        {
            Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = jugador.CuotaPorMes.Values.ToList(), MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
            });
        }

        var filas = FilasDelJugador(jugador.Id);
        Assert.Equal(3, filas.Count);
        Assert.All(filas, f =>
        {
            Assert.True(f.Estado);
            Assert.Equal("Efectivo", f.MetodoPago);
            Assert.Equal(operador.Id, f.IdUsuarioRegistro);
        });
        Assert.Single(filas.Select(f => f.FechaHoraRegistro).Distinct());
    }

    // Varias cuotas, y la operación falla A MITAD de camino: la primera cuota ya se había ajustado con su beca cuando la
    // tercera resulta cubierta al 100 % y el cobro se rechaza. No queda nada a medias: ni abonadas, ni montos cambiados,
    // ni rastro en la auditoría.
    [CobroRealFact]
    public void VariasCuotas_SiLaOperacionFallaNoQuedaNingunaAMedias()
    {
        var jugador = CrearJugadorConCuotas(1, 2, 3);
        AsignarBeca(jugador.Id, 50m, desdeMes: 1, hastaMes: 1);   // enero: se ajusta (escribe) antes de fallar
        AsignarBeca(jugador.Id, 100m, desdeMes: 3, hastaMes: 3);  // marzo: cubierta, el cobro se rechaza
        var operador = Operador();
        var antes = FilasDelJugador(jugador.Id);
        int auditoriaAntes = jugador.CuotaPorMes.Values.Sum(id => AuditoriaDelPago(id).Count);

        using (SqlAuditActorContext.Push(operador.Id))
        {
            var error = Assert.Throws<CobroInvalidoException>(() => Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = jugador.CuotaPorMes.Values.ToList(), MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
            }));
            Assert.Contains("cubierta por un beneficio", error.Message);
        }

        Assert.Equal(antes, FilasDelJugador(jugador.Id));
        Assert.All(FilasDelJugador(jugador.Id), f =>
        {
            Assert.False(f.Estado);
            Assert.Equal(Cuota, f.MontoFinal);
            Assert.Null(f.IdDescuento);
        });
        Assert.Equal(auditoriaAntes, jugador.CuotaPorMes.Values.Sum(id => AuditoriaDelPago(id).Count));
    }

    // Varias cuotas, y una ya la había cobrado otro administrador (pantalla desactualizada): se rechaza todo el lote y
    // las otras siguen pendientes.
    [CobroRealFact]
    public void VariasCuotas_SiUnaYaFueCobradaPorOtroSeRechazaTodoElLote()
    {
        var jugador = CrearJugadorConCuotas(9, 10, 11);
        var operador = Operador();

        using (SqlAuditActorContext.Push(operador.Id))
        {
            Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = new List<int> { jugador.CuotaPorMes[10] }, MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
            });

            var error = Assert.Throws<CobroInvalidoException>(() => Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = jugador.CuotaPorMes.Values.ToList(), MetodoPago = "Transferencia", IdUsuarioRegistro = operador.Id
            }));
            Assert.Contains("ya fueron abonadas", error.Message);
        }

        var filas = FilasDelJugador(jugador.Id);
        Assert.False(filas.Single(f => f.Id == jugador.CuotaPorMes[9]).Estado);
        Assert.False(filas.Single(f => f.Id == jugador.CuotaPorMes[11]).Estado);
        Assert.Equal("Efectivo", filas.Single(f => f.Id == jugador.CuotaPorMes[10]).MetodoPago); // la cobrada antes no cambió
    }

    // Con beneficio: el cobro registra el monto ajustado y el beneficio aplicado, y el saldo que ve el Administrativo
    // (estado de cuenta) es el mismo que lee el Deportivo (estado de deuda), antes y después del pago.
    [CobroRealFact]
    public void ConBeneficio_SeCobraElMontoAjustadoYLosDosSistemasVenElMismoSaldo()
    {
        var jugador = CrearJugadorConCuotas(4, 5);
        int beca = AsignarBeca(jugador.Id, 50m, desdeMes: 4, hastaMes: 5);
        var operador = Operador();

        decimal saldoAdministrativoAntes = Servicio().GetPlayerStatement(jugador.Id)!.TotalDebtAmount;
        Assert.Equal(Cuota, saldoAdministrativoAntes);   // 2 cuotas de $20.000 con el 50 %
        Assert.Equal(saldoAdministrativoAntes, EstadoQueLeeElDeportivo(jugador.Dni).DeudaTotal);

        CobrarPagosPendientesResultado resultado;
        using (SqlAuditActorContext.Push(operador.Id))
        {
            resultado = Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = new List<int> { jugador.CuotaPorMes[4] }, MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
            });
        }

        Assert.Equal(Cuota / 2, resultado.MontoTotal);
        var abril = FilasDelJugador(jugador.Id).Single(f => f.Id == jugador.CuotaPorMes[4]);
        Assert.True(abril.Estado);
        Assert.Equal(Cuota / 2, abril.MontoFinal);
        Assert.Equal(beca, abril.IdDescuento);

        decimal saldoAdministrativoDespues = Servicio().GetPlayerStatement(jugador.Id)!.TotalDebtAmount;
        Assert.Equal(Cuota / 2, saldoAdministrativoDespues);   // queda mayo, con el 50 %
        Assert.Equal(saldoAdministrativoDespues, EstadoQueLeeElDeportivo(jugador.Dni).DeudaTotal);
    }

    // Historial: el pago aparece en el historial del jugador (HU-017) y en Actividad y Movimientos, con su método,
    // monto y responsable.
    [CobroRealFact]
    public void Historial_ElPagoApareceEnElHistorialDelJugadorYEnActividad()
    {
        var jugador = CrearJugadorConCuotas(6);
        var operador = Operador();

        using (SqlAuditActorContext.Push(operador.Id))
        {
            Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = new List<int> { jugador.CuotaPorMes[6] }, MetodoPago = "Transferencia", IdUsuarioRegistro = operador.Id
            });
        }

        var historial = new PagosDao(Conexion).ObtenerHistorialPagos(jugador.Id, 1, 10);
        var junio = Assert.Single(historial.Items);
        Assert.Equal("Junio 2025", junio.Periodo);
        Assert.Equal(Cuota, junio.MontoTotal);
        var abono = Assert.Single(junio.Abonos);
        Assert.Equal(jugador.CuotaPorMes[6], abono.IdPago);
        Assert.Equal("Transferencia", abono.MetodoPago);
        Assert.False(string.IsNullOrWhiteSpace(abono.ResponsableNombre) && string.IsNullOrWhiteSpace(abono.ResponsableApellido),
            "El historial no muestra quién registró el pago.");

        var actividad = new PagosDao(Conexion).ObtenerPagosRegistrados(new FiltroPagosRegistrados { Busqueda = jugador.Apellido });
        var registrado = Assert.Single(actividad.Items);
        Assert.Equal(jugador.CuotaPorMes[6], registrado.IdPago);
        Assert.Equal("Transferencia", registrado.MetodoPago);
        Assert.Equal(Cuota, registrado.Monto);
        Assert.Equal("Pagado", registrado.Estado);
    }

    // Lectura del otro sistema: después de cada pago, el estado de deuda que consulta el Deportivo ya es el nuevo
    // (Inhabilitado -> Solo entrenamientos -> Habilitado), sin datos viejos.
    [CobroRealFact]
    public void LecturaDelDeportivo_VeElEstadoNuevoDespuesDeCadaPago()
    {
        var jugador = CrearJugadorConCuotas(7, 8);
        var operador = Operador();

        var inicial = EstadoQueLeeElDeportivo(jugador.Dni);
        Assert.Equal(2, inicial.CuotasVencidas);
        Assert.Equal(EstadoDeportivo.Inhabilitado, inicial.Estado);

        foreach (var (mes, vencidasEsperadas, estadoEsperado) in new[]
                 { (7, 1, EstadoDeportivo.SoloEntrenamientos), (8, 0, EstadoDeportivo.Habilitado) })
        {
            using (SqlAuditActorContext.Push(operador.Id))
            {
                Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
                {
                    IdJugador = jugador.Id, IdsPago = new List<int> { jugador.CuotaPorMes[mes] }, MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
                });
            }

            var leido = EstadoQueLeeElDeportivo(jugador.Dni);
            Assert.Equal(vencidasEsperadas, leido.CuotasVencidas);
            Assert.Equal(estadoEsperado, leido.Estado);
            Assert.Equal(vencidasEsperadas * Cuota, leido.DeudaTotal);
        }
    }

    // Auditoría: el cobro queda en AUDITORIA_CAMBIOS con el usuario que lo registró y el método en los datos nuevos.
    [CobroRealFact]
    public void Auditoria_ElCobroQuedaRegistradoConSuUsuario()
    {
        var jugador = CrearJugadorConCuotas(9);
        var operador = Operador();

        using (SqlAuditActorContext.Push(operador.Id))
        {
            Servicio().CobrarPagosPendientes(new CobrarPagosPendientesRequest
            {
                IdJugador = jugador.Id, IdsPago = new List<int> { jugador.CuotaPorMes[9] }, MetodoPago = "Transferencia", IdUsuarioRegistro = operador.Id
            });
        }

        var cobro = Assert.Single(AuditoriaDelPago(jugador.CuotaPorMes[9]), e => e.Accion == "UPDATE");
        Assert.Equal(operador.Id, cobro.IdUsuario);
        Assert.Equal(operador.Email, cobro.Email);
        Assert.Contains("\"metodoPago\":\"Transferencia\"", cobro.DatosDespues);
        Assert.Contains("\"estado\":true", cobro.DatosDespues);
    }

    // Auditoría del registro por el total: el abono nuevo (INSERT) y la cuota pendiente que se cierra (DELETE) quedan
    // los dos con el usuario que lo registró.
    [CobroRealFact]
    public void Auditoria_ElRegistroDejaElAltaDelAbonoYElCierreDeLaCuotaConSuUsuario()
    {
        var jugador = CrearJugadorConCuotas(10);
        var operador = Operador();

        RegistrarPagoResultado resultado;
        using (SqlAuditActorContext.Push(operador.Id))
        {
            resultado = Servicio().RegistrarPago(new RegistrarPagoRequest
            {
                IdJugador = jugador.Id, Periodo = "Octubre", Anio = Anio, Monto = Cuota, MetodoPago = "Efectivo", IdUsuarioRegistro = operador.Id
            });
        }

        var alta = Assert.Single(AuditoriaDelPago(resultado.IdPago), e => e.Accion == "INSERT");
        Assert.Equal(operador.Id, alta.IdUsuario);
        Assert.Contains("\"metodoPago\":\"Efectivo\"", alta.DatosDespues);

        var cierre = Assert.Single(AuditoriaDelPago(jugador.CuotaPorMes[10]), e => e.Accion == "DELETE");
        Assert.Equal(operador.Id, cierre.IdUsuario);
    }
}

// HU-063, sin escritura cruzada: el Portal Deportivo usa el mismo token que el Administrativo, así que lo que lo frena es
// la autorización de cada endpoint. Ningún rol del Deportivo puede registrar, modificar ni anular pagos. No necesita base.
public class PagosSinEscrituraCruzadaTests
{
    // Roles de ROLES que solo existen en el Portal Deportivo. "1" (SuperAdmin del Administrativo) no es de ellos.
    private static readonly string[] RolesDelDeportivo =
    {
        ApiGestion.Services.RolesDelDeportivo.Administrador, "Administrativo", ApiGestion.Services.RolesDelDeportivo.DonQr,
        ApiGestion.Services.RolesDelDeportivo.Coordinador, ApiGestion.Services.RolesDelDeportivo.Medico,
        ApiGestion.Services.RolesDelDeportivo.DirectorTecnico, ApiGestion.Services.RolesDelDeportivo.PreparadorFisico
    };

    // Los únicos POST que un rol del Deportivo puede llamar, y por qué ninguno registra ni modifica pagos:
    //  - estado-deuda/lote (PorLote): es una consulta (POST solo para mandar la lista de DNI).
    //  - cargos-alta: contrato de integración v1. Genera la inscripción y la cuota PENDIENTES de un jugador que el
    //    Deportivo dio de alta; no cobra ni toca pagos existentes.
    private static readonly string[] EscriturasPermitidasAlDeportivo =
    {
        $"{nameof(EstadoDeudaController)}.{nameof(EstadoDeudaController.PorLote)}",
        $"{nameof(IntegracionJugadoresController)}.{nameof(IntegracionJugadoresController.CargosDeAlta)}"
    };

    private static IEnumerable<(Type Controlador, MethodInfo Accion)> AccionesQueEscriben() =>
        typeof(PagosController).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Name.EndsWith("Controller") && t != typeof(AuthController))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().SelectMany(a => a.HttpMethods).Any(v => v != "GET"))
                .Select(m => (t, m)));

    // ASP.NET exige TODOS los [Authorize] (el de la clase y el de la acción). Uno sin Roles admite a cualquier sesión.
    private static bool AdmiteAlRol(Type controlador, MethodInfo accion, string rol)
    {
        if (accion.GetCustomAttribute<AllowAnonymousAttribute>() != null || controlador.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            return true;
        var exigidos = controlador.GetCustomAttributes<AuthorizeAttribute>(true).Concat(accion.GetCustomAttributes<AuthorizeAttribute>()).ToList();
        if (exigidos.Count == 0) return true;
        return exigidos.All(a => a.Roles is null || a.Roles.Split(',').Select(r => r.Trim()).Contains(rol));
    }

    [Fact]
    public void ElDeportivoNoPuedeRegistrarModificarNiAnularPagos()
    {
        var escrituras = AccionesQueEscriben().ToList();
        Assert.Contains(escrituras, e => e.Controlador == typeof(PagosController) && e.Accion.Name == nameof(PagosController.CobrarPagosPendientes));

        var abiertasAlDeportivo = escrituras
            .Where(e => RolesDelDeportivo.Any(rol => AdmiteAlRol(e.Controlador, e.Accion, rol)))
            .Select(e => $"{e.Controlador.Name}.{e.Accion.Name}")
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(EscriturasPermitidasAlDeportivo.OrderBy(n => n), abiertasAlDeportivo);
    }

    [Theory]
    [InlineData(nameof(PagosController.RegistrarPago))]
    [InlineData(nameof(PagosController.CobrarPagosPendientes))]
    [InlineData(nameof(PagosController.AnularPago))]
    public void CadaEscrituraDePagosEsSoloDelAdministrativo(string accion)
    {
        var metodo = typeof(PagosController).GetMethod(accion)!;
        Assert.All(RolesDelDeportivo, rol => Assert.False(AdmiteAlRol(typeof(PagosController), metodo, rol), $"{accion} admite a {rol}"));
        Assert.True(AdmiteAlRol(typeof(PagosController), metodo, "1"));
    }
}
