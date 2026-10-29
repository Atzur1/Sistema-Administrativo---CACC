namespace ApiGestion.Tests;

using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using ServiceLibrary;

// HU-057: la baja que informa el Portal Deportivo y la reincorporación, contra la base unificada real. El servicio, el DAO, el
// SQL y los triggers (auditoría y JUGADORES_PERIODOS) corren de verdad. El reloj se fija en 2030 y las cuotas salen de
// categorías y aranceles propios de estas pruebas, para no mezclarse con datos reales.
// Estas pruebas COMMITEAN (la concurrencia así lo exige): los DNI son del rango reservado 9857xxxx y todo lo que cargan se
// limpia al empezar y al terminar.
[Collection(BaseDeDatosCollection.Nombre)]
public class BajaJugadoresIntegracionIntegrationTests : IDisposable
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static readonly DateTime Alta = new(2030, 1, 5);
    private static readonly DateTime FechaBaja = new(2030, 3, 15);
    private static readonly DateTime Hoy = new(2030, 3, 20);
    private static readonly DateTime Enero = new(2030, 1, 1);
    private static readonly DateTime Febrero = new(2030, 2, 1);
    private static readonly DateTime Marzo = new(2030, 3, 1);
    private static readonly DateTime Abril = new(2030, 4, 1);
    private static readonly DateTime Mayo = new(2030, 5, 1);
    private const decimal ArancelA = 1_000m;
    private const decimal ArancelB = 2_000m;

    private readonly int _categoria;
    private readonly int _categoriaNueva;

    public BajaJugadoresIntegracionIntegrationTests()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable))) return;
        Limpiar();
        _categoria = CrearCategoria("HU057 Prueba A");
        _categoriaNueva = CrearCategoria("HU057 Prueba B");
        Ejecutar("INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (NULL, @c, @m, '2030-01-01')", ("@c", _categoria), ("@m", ArancelA));
        Ejecutar("INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (NULL, @c, @m, '2030-01-01')", ("@c", _categoriaNueva), ("@m", ArancelB));
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable))) return;
        Limpiar();
    }

    // ===== Baja: lógica, con fecha, sin perder historia =====

    [IntegracionFact]
    public void LaBaja_RegistraLaFecha_SinBorrarAlJugadorNiLaPersona_NiTocarLaDeudaYLosPagosAnteriores()
    {
        int jugador = CrearJugador(1, _categoria, Alta);
        Cuota(jugador, Enero, 1_000m);                 // vencida
        Cuota(jugador, Febrero, 600m);                 // parcial: 400 abonados
        Abono(jugador, Febrero, 400m);
        Abono(jugador, Marzo, 1_000m);                 // pagada
        string antes = Pagos(jugador);

        var resultado = Servicio(Hoy).DarDeBaja(jugador, "2030-03-15");

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.Equal(jugador, resultado.Valor!.IdJugador);
        Assert.Equal(FechaBaja, resultado.Valor.FechaBaja);
        Assert.False(resultado.Valor.YaEstabaDadoDeBaja);

        Assert.Equal(FechaBaja, Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal(0, Contar("SELECT CAST(activo AS INT) FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PERSONAS WHERE Dni = @d", ("@d", Dni(1))));
        Assert.Equal(antes, Pagos(jugador));
        // La permanencia queda cerrada en el historial, no borrada.
        Assert.Equal("2030-01-05..2030-03-15", Periodos(jugador));
    }

    [IntegracionFact]
    public void DeudasYMorosidad_SigueMostrandoLaDeudaDelJugadorDadoDeBaja()
    {
        int jugador = CrearJugador(2, _categoria, Alta);
        Cuota(jugador, Enero, 1_000m);
        Cuota(jugador, Febrero, 1_000m);

        Assert.True(Servicio(Hoy).DarDeBaja(jugador, "2030-03-15").Exito);

        var deudor = Assert.Single(new PagosDao(Conexion).ObtenerPendientesAgrupados(_categoria), d => d.IdJugador == jugador);
        Assert.Equal(2_000m, deudor.MontoTotal);
        Assert.Equal(2, deudor.CantidadCuotas);
        Assert.Equal(FechaBaja, deudor.FechaBaja);
    }

    [IntegracionFact]
    public void LaBecaVigente_SeCancelaConLaFechaDeBaja_SinBorrarse()
    {
        int jugador = CrearJugador(3, _categoria, Alta);
        int beca = Convert.ToInt32(Escalar(@"
            INSERT INTO JUGADORES_DESCUENTOS (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor, porcentaje, fecha_inicio, fecha_fin, fecha_asignacion)
            VALUES (@j, 1, 1, '%', 50, '2030-01-01', '2030-12-31', '2030-01-01'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@j", jugador)));

        Assert.True(Servicio(Hoy).DarDeBaja(jugador, "2030-03-15").Exito);

        Assert.Equal("0|1|2030-03-15", Texto(
            "SELECT CONCAT(CAST(estado_activo AS INT), '|', CAST(cancelada_por_baja AS INT), '|', CONVERT(CHAR(10), fecha_cancelacion, 23)) FROM JUGADORES_DESCUENTOS WHERE PK_id_jugador_descuento = @b",
            ("@b", beca)));
    }

    // ===== Generación de cuotas: el dado de baja no entra en el conjunto elegible =====

    [IntegracionFact]
    public void ElGeneradorAutomatico_YLaGeneracionManual_NoLeEmitenCuotasDeMesesPosterioresALaBaja()
    {
        int deBaja = CrearJugador(4, _categoria, Alta);
        int activo = CrearJugador(5, _categoria, Alta);
        Assert.True(Servicio(Hoy).DarDeBaja(deBaja, "2030-03-15").Exito);

        var pagos = new PagosDao(Conexion);
        pagos.GenerarCuotasPendientesDelMes(null, _categoria, Marzo.Month, Marzo.Year);   // generador mensual (HU-049)
        pagos.GenerarCuotasPendientesDelMes(null, _categoria, Abril.Month, Abril.Year);
        new CuotasFaltantesDao(Conexion).GenerarFaltantesDelMes(Mayo);                   // "generar faltantes" del Resumen

        // Marzo: estuvo en el club parte del mes, le corresponde (misma regla que el alta).
        Assert.Equal(1, CuotasDelMes(deBaja, Marzo));
        Assert.Equal(0, CuotasDelMes(deBaja, Abril));
        Assert.Equal(0, CuotasDelMes(deBaja, Mayo));
        Assert.Equal(1, CuotasDelMes(activo, Abril));
        Assert.Equal(1, CuotasDelMes(activo, Mayo));

        // Cobro por adelantado: emitir la cuota de un mes posterior a la baja tampoco es posible.
        using var conexion = SqlConnectionFactory.Open(Conexion);
        using var transaccion = conexion.BeginTransaction();
        Assert.False(pagos.EmitirCuotaDeJugador(conexion, transaccion, deBaja, Abril.Month, Abril.Year));
        transaccion.Rollback();
    }

    // ===== Validaciones =====

    [IntegracionTheory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("15/03/2030")]
    [InlineData("2030-02-30")]
    [InlineData("2030-03-21")]  // futura respecto de hoy (2030-03-20)
    public void UnaFechaDeBajaInvalida_Es400ConElCampo_YNoCambiaNada(string? fecha)
    {
        int jugador = CrearJugador(6, _categoria, Alta);

        var resultado = Servicio(Hoy).DarDeBaja(jugador, fecha);

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Equal("fechaBaja", resultado.Campo);
        Assert.Null(Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
    }

    [IntegracionFact]
    public void UnaBajaAnteriorAlAlta_Es422_YNoCambiaNada()
    {
        int jugador = CrearJugador(7, _categoria, Alta);

        var resultado = Servicio(Hoy).DarDeBaja(jugador, "2030-01-04");

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Equal("fechaBaja", resultado.Campo);
        Assert.Contains("05/01/2030", resultado.Mensaje);
        Assert.Null(Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
    }

    // Los jugadores que trajo la unificación (HU-053) no tienen fecha_alta: su permanencia empieza el día de la importación
    // (JUGADORES_PERIODOS). Una baja anterior a ese día violaba el CHECK del período y terminaba en un error 500.
    [IntegracionFact]
    public void UnJugadorSinFechaDeAlta_ConUnaBajaAnteriorAlInicioDeSuPermanencia_Es422_NoUnError()
    {
        int jugador = CrearJugador(8, _categoria, null);
        Ejecutar("UPDATE JUGADORES_PERIODOS SET fecha_alta = '2030-02-01' WHERE FK_id_jugador = @j AND fecha_baja IS NULL", ("@j", jugador));

        var resultado = Servicio(Hoy).DarDeBaja(jugador, "2030-01-20");

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("01/02/2030", resultado.Mensaje);
        Assert.Null(Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
    }

    [IntegracionTheory]
    [InlineData(0, TipoResultado.Invalido, "idJugador")]
    [InlineData(-5, TipoResultado.Invalido, "idJugador")]
    [InlineData(999_999_999, TipoResultado.NoEncontrado, null)]
    public void UnJugadorInvalidoOInexistente_SeRechaza(int idJugador, TipoResultado esperado, string? campo)
    {
        var resultado = Servicio(Hoy).DarDeBaja(idJugador, "2030-03-15");

        Assert.Equal(esperado, resultado.Tipo);
        Assert.Equal(campo, resultado.Campo);
    }

    // ===== Idempotencia y concurrencia =====

    [IntegracionFact]
    public void ElReintentoConLaMismaFecha_EsOk_SinUnaSegundaBajaNiOtroEventoNiCambiosEnLosPagos()
    {
        int jugador = CrearJugador(9, _categoria, Alta);
        Cuota(jugador, Enero, 1_000m);
        var servicio = Servicio(Hoy);

        Assert.False(servicio.DarDeBaja(jugador, "2030-03-15").Valor!.YaEstabaDadoDeBaja);
        string despuesDeLaPrimera = Pagos(jugador);
        var reintento = servicio.DarDeBaja(jugador, "2030-03-15");

        Assert.True(reintento.Exito, reintento.Mensaje);
        Assert.True(reintento.Valor!.YaEstabaDadoDeBaja);
        Assert.Equal(FechaBaja, reintento.Valor.FechaBaja);
        Assert.Equal(despuesDeLaPrimera, Pagos(jugador));
        Assert.Equal(1, EventosDeBaja(jugador));
        Assert.Equal("2030-01-05..2030-03-15", Periodos(jugador));
    }

    [IntegracionFact]
    public void UnaSegundaBajaConOtraFecha_Es409_YConservaLaPrimera()
    {
        int jugador = CrearJugador(10, _categoria, Alta);
        var servicio = Servicio(Hoy);
        Assert.True(servicio.DarDeBaja(jugador, "2030-03-15").Exito);

        var otra = servicio.DarDeBaja(jugador, "2030-03-10");

        Assert.Equal(TipoResultado.Conflicto, otra.Tipo);
        Assert.Contains("15/03/2030", otra.Mensaje);
        Assert.Equal(FechaBaja, Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal(1, EventosDeBaja(jugador));
    }

    [IntegracionFact]
    public void OchoBajasSimultaneasDelMismoJugador_DejanUnaSolaBajaEfectiva()
    {
        int jugador = CrearJugador(11, _categoria, Alta);
        var servicio = Servicio(Hoy);

        var resultados = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(() => servicio.DarDeBaja(jugador, "2030-03-15")))
            .ToArray();
        Task.WaitAll(resultados);

        Assert.All(resultados, r => Assert.True(r.Result.Exito, r.Result.Mensaje));
        Assert.Equal(1, resultados.Count(r => !r.Result.Valor!.YaEstabaDadoDeBaja));
        Assert.Equal(1, EventosDeBaja(jugador));
        Assert.Equal("2030-01-05..2030-03-15", Periodos(jugador));
    }

    // La baja y la generación toman el mismo bloqueo de escritura de cobros (BloqueoEscrituraPagos): sea cual sea el orden, el
    // jugador termina sin cuota de abril y con la de marzo.
    [IntegracionFact]
    public void LaBajaMientrasCorreLaGeneracionMensual_TerminaSinCuotasPosterioresALaBaja()
    {
        int jugador = CrearJugador(12, _categoria, Alta);
        var pagos = new PagosDao(Conexion);
        var servicio = Servicio(Hoy);

        var tareas = new[]
        {
            Task.Run(() => pagos.GenerarCuotasPendientesDelMes(null, _categoria, Abril.Month, Abril.Year)),
            Task.Run(() => Assert.True(servicio.DarDeBaja(jugador, "2030-03-15").Exito)),
            Task.Run(() => pagos.GenerarCuotasPendientesDelMes(null, _categoria, Marzo.Month, Marzo.Year))
        };
        Task.WaitAll(tareas);

        Assert.Equal(1, CuotasDelMes(jugador, Marzo));
        Assert.Equal(0, CuotasDelMes(jugador, Abril));
    }

    // ===== Auditoría =====

    [IntegracionFact]
    public void LaBaja_QuedaAuditadaConElDeportivoComoOrigen_ElOperador_YLaFechaEfectiva()
    {
        int jugador = CrearJugador(13, _categoria, Alta);
        int operador = Convert.ToInt32(Escalar("SELECT TOP (1) PK_id_usuario FROM USUARIOS ORDER BY PK_id_usuario"));

        using (SqlAuditActorContext.Push(operador))
        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            Assert.True(Servicio(Hoy).DarDeBaja(jugador, "2030-03-15").Exito);
        }

        string evento = Texto(@"
            SELECT TOP (1) CONCAT(accion, '|', origen_sistema, '|', id_usuario, '|', JSON_VALUE(datos_antes, '$.fecha_baja'), '|', JSON_VALUE(datos_despues, '$.fecha_baja'))
            FROM AUDITORIA_CAMBIOS WHERE entidad = N'JUGADORES' AND id_entidad = @j ORDER BY PK_id_evento DESC", ("@j", jugador.ToString()))!;
        Assert.Equal($"UPDATE|{IntegrationClientCredentials.SistemaDeportivo}|{operador}||2030-03-15", evento);
    }

    [IntegracionFact]
    public void UnaBajaRechazada_NoDejaNingunEventoDeBaja()
    {
        int jugador = CrearJugador(14, _categoria, Alta);

        Assert.False(Servicio(Hoy).DarDeBaja(jugador, "2030-01-01").Exito);

        Assert.Equal(0, EventosDeBaja(jugador));
    }

    // ===== Reincorporación =====

    [IntegracionFact]
    public void LaReincorporacionAntesDelDia10_ReutilizaJugadorYPersona_NoGeneraLosMesesDeBaja_NiLaCuotaDelMesTodavia()
    {
        int jugador = CrearJugador(15, _categoria, Alta);
        int persona = Contar("SELECT FK_id_persona FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador));
        Cuota(jugador, Enero, 1_000m);
        Assert.True(Servicio(new DateTime(2030, 2, 20)).DarDeBaja(jugador, "2030-02-15").Exito);
        string historia = Pagos(jugador);

        var resultado = Servicio(new DateTime(2030, 5, 5)).Reincorporar(jugador, null);

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.Equal(new DateTime(2030, 5, 5), resultado.Valor!.FechaAlta);
        Assert.False(resultado.Valor.CategoriaCambiada);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d", ("@d", Dni(15))));
        Assert.Equal(persona, Contar("SELECT FK_id_persona FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Null(Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal(historia, Pagos(jugador));                 // ni marzo, ni abril, ni mayo (antes del 10)
        Assert.Equal("2030-01-05..2030-02-15|2030-05-05..", Periodos(jugador));

        // El día 10 el generador le emite mayo como a todos, una sola vez; los meses de baja siguen sin cuota.
        var pagos = new PagosDao(Conexion);
        pagos.GenerarCuotasPendientesDelMes(null, _categoria, Mayo.Month, Mayo.Year);
        pagos.GenerarCuotasPendientesDelMes(null, _categoria, Mayo.Month, Mayo.Year);
        new CuotasFaltantesDao(Conexion).GenerarFaltantesDelMes(Marzo);
        new CuotasFaltantesDao(Conexion).GenerarFaltantesDelMes(Abril);
        Assert.Equal(1, CuotasDelMes(jugador, Mayo));
        Assert.Equal(0, CuotasDelMes(jugador, Marzo));
        Assert.Equal(0, CuotasDelMes(jugador, Abril));
    }

    [IntegracionFact]
    public void LaReincorporacionDesdeElDia10_EmiteLaCuotaDelMesEnElMomento_UnaSolaVez()
    {
        int jugador = CrearJugador(16, _categoria, Alta);
        Assert.True(Servicio(new DateTime(2030, 2, 20)).DarDeBaja(jugador, "2030-02-15").Exito);

        Assert.True(Servicio(new DateTime(2030, 5, 12)).Reincorporar(jugador, null).Exito);
        new PagosDao(Conexion).GenerarCuotasPendientesDelMes(null, _categoria, Mayo.Month, Mayo.Year);

        Assert.Equal(1, CuotasDelMes(jugador, Mayo));
        Assert.Equal(ArancelA, Convert.ToDecimal(Escalar(
            "SELECT monto_base FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Cuota' AND fecha_vencimiento = @m", ("@j", jugador), ("@m", Mayo))));
        Assert.Equal(0, CuotasDelMes(jugador, Abril));
    }

    [IntegracionFact]
    public void LaReincorporacionConOtraCategoria_LaActualiza_SinTocarLasCuotasAnteriores_YLaNuevaCuotaUsaSuArancel()
    {
        int jugador = CrearJugador(17, _categoria, Alta);
        Cuota(jugador, Enero, ArancelA);
        Assert.True(Servicio(new DateTime(2030, 2, 20)).DarDeBaja(jugador, "2030-02-15").Exito);
        string historia = Pagos(jugador);

        var resultado = Servicio(new DateTime(2030, 5, 12)).Reincorporar(jugador, _categoriaNueva);

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.True(resultado.Valor!.CategoriaCambiada);
        Assert.Equal(_categoriaNueva, Contar("SELECT FK_id_categoria FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador = @j AND FK_id_categoria = @c",
            ("@j", jugador), ("@c", _categoriaNueva)));
        Assert.StartsWith(historia, Pagos(jugador));            // enero conserva su monto
        Assert.Equal(ArancelB, Convert.ToDecimal(Escalar(
            "SELECT monto_base FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Cuota' AND fecha_vencimiento = @m", ("@j", jugador), ("@m", Mayo))));
    }

    [IntegracionFact]
    public void ReincorporarAUnJugadorActivo_Es409_YNoCambiaNada()
    {
        int jugador = CrearJugador(18, _categoria, Alta);

        var resultado = Servicio(Hoy).Reincorporar(jugador, null);

        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
        Assert.Equal(Alta, Fecha("SELECT fecha_alta FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal("2030-01-05..", Periodos(jugador));
    }

    [IntegracionTheory]
    [InlineData(0, TipoResultado.Invalido)]
    [InlineData(999_999, TipoResultado.NoProcesable)]
    public void ReincorporarConUnaCategoriaInvalida_SeRechazaConElCampo_YSigueDeBaja(int categoria, TipoResultado esperado)
    {
        int jugador = CrearJugador(19, _categoria, Alta);
        Assert.True(Servicio(Hoy).DarDeBaja(jugador, "2030-03-15").Exito);

        var resultado = Servicio(new DateTime(2030, 5, 12)).Reincorporar(jugador, categoria);

        Assert.Equal(esperado, resultado.Tipo);
        Assert.Equal("idCategoria", resultado.Campo);
        Assert.Equal(FechaBaja, Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.Equal(0, CuotasDelMes(jugador, Mayo));
    }

    [IntegracionFact]
    public void ReincorporarAUnJugadorInexistente_Es404()
    {
        Assert.Equal(TipoResultado.NoEncontrado, Servicio(Hoy).Reincorporar(999_999_999, null).Tipo);
        Assert.Equal(TipoResultado.Invalido, Servicio(Hoy).Reincorporar(0, null).Tipo);
    }

    [IntegracionFact]
    public void DosReincorporacionesSimultaneas_DejanUnSoloPeriodoAbiertoYUnaSolaCuota()
    {
        int jugador = CrearJugador(20, _categoria, Alta);
        Assert.True(Servicio(new DateTime(2030, 2, 20)).DarDeBaja(jugador, "2030-02-15").Exito);
        var servicio = Servicio(new DateTime(2030, 5, 12));

        var resultados = Enumerable.Range(0, 4).Select(_ => Task.Run(() => servicio.Reincorporar(jugador, null))).ToArray();
        Task.WaitAll(resultados);

        Assert.Equal(1, resultados.Count(r => r.Result.Exito));
        Assert.All(resultados.Where(r => !r.Result.Exito), r => Assert.Equal(TipoResultado.Conflicto, r.Result.Tipo));
        Assert.Equal("2030-01-05..2030-02-15|2030-05-12..", Periodos(jugador));
        Assert.Equal(1, CuotasDelMes(jugador, Mayo));
    }

    [IntegracionFact]
    public void LaReincorporacion_QuedaAuditadaConElDeportivoComoOrigen()
    {
        int jugador = CrearJugador(21, _categoria, Alta);
        Assert.True(Servicio(new DateTime(2030, 2, 20)).DarDeBaja(jugador, "2030-02-15").Exito);

        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            Assert.True(Servicio(new DateTime(2030, 5, 5)).Reincorporar(jugador, null).Exito);
        }

        Assert.Equal($"{IntegrationClientCredentials.SistemaDeportivo}|2030-02-15||2030-05-05", Texto(@"
            SELECT TOP (1) CONCAT(origen_sistema, '|', JSON_VALUE(datos_antes, '$.fecha_baja'), '|', JSON_VALUE(datos_despues, '$.fecha_baja'), '|', JSON_VALUE(datos_despues, '$.fecha_alta'))
            FROM AUDITORIA_CAMBIOS WHERE entidad = N'JUGADORES' AND id_entidad = @j ORDER BY PK_id_evento DESC", ("@j", jugador.ToString())));
    }

    // ===== Ayudas =====

    private static JugadoresBajaService Servicio(DateTime hoy)
        => new(new JugadoresDao(Conexion), new CategoriasDao(Conexion), () => hoy);

    private static string Dni(int n) => (98570000 + n).ToString();

    private static int CrearCategoria(string nombre)
        => Convert.ToInt32(Escalar("INSERT INTO CATEGORIAS (nombre_categoria, asociacion) VALUES (@n, 'HU057'); SELECT CAST(SCOPE_IDENTITY() AS INT);", ("@n", nombre)));

    // Persona y jugador directo en la base (alta null = como los que trajo la unificación).
    private static int CrearJugador(int n, int categoria, DateTime? alta)
    {
        Ejecutar("INSERT INTO PERSONAS (nombre, apellido, Dni, genero, fecha_de_nacimiento) VALUES (N'Prueba', N'Baja', @d, 'Masculino', '2012-01-01')",
            ("@d", Dni(n)));
        return Convert.ToInt32(Escalar(@"
            DECLARE @id INT = NEXT VALUE FOR dbo.SEQ_JUGADORES;
            INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria, fecha_alta)
            SELECT @id, PK_id_persona, @c, @f FROM PERSONAS WHERE Dni = @d;
            SELECT @id;", ("@c", categoria), ("@f", (object?)alta ?? DBNull.Value), ("@d", Dni(n))));
    }

    private static void Cuota(int jugador, DateTime periodo, decimal saldo)
        => Ejecutar("INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto) VALUES (@j, @b, @s, @p, 0, 'Cuota')",
            ("@j", jugador), ("@b", ArancelA), ("@s", saldo), ("@p", periodo));

    private static void Abono(int jugador, DateTime periodo, decimal monto)
        => Ejecutar(@"INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
                      VALUES (@j, @b, @m, @f, 'Efectivo', @p, 1, 'Cuota')",
            ("@j", jugador), ("@b", ArancelA), ("@m", monto), ("@f", periodo.AddDays(4)), ("@p", periodo));

    private static string Pagos(int jugador) => string.Join("|", Lista(
        "SELECT CONCAT(PK_id_pago, ':', concepto, ':', CONVERT(CHAR(10), fecha_vencimiento, 23), ':', monto_base, ':', monto_final, ':', estado, ':', metodo_pago) FROM PAGOS WHERE FK_id_jugador = @j ORDER BY PK_id_pago",
        ("@j", jugador)));

    private static int CuotasDelMes(int jugador, DateTime periodo) => Contar(
        "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Cuota' AND estado = 0 AND fecha_vencimiento = @p",
        ("@j", jugador), ("@p", periodo));

    private static string Periodos(int jugador) => string.Join("|", Lista(
        "SELECT CONCAT(CONVERT(CHAR(10), fecha_alta, 23), '..', CONVERT(CHAR(10), fecha_baja, 23)) FROM JUGADORES_PERIODOS WHERE FK_id_jugador = @j ORDER BY fecha_alta",
        ("@j", jugador)));

    // Eventos de auditoría en los que fecha_baja pasó de vacía a una fecha.
    private static int EventosDeBaja(int jugador) => Contar(@"
        SELECT COUNT(*) FROM AUDITORIA_CAMBIOS
        WHERE entidad = N'JUGADORES' AND id_entidad = @j AND accion = 'UPDATE'
          AND JSON_VALUE(datos_antes, '$.fecha_baja') IS NULL AND JSON_VALUE(datos_despues, '$.fecha_baja') IS NOT NULL",
        ("@j", jugador.ToString()));

    private static void Limpiar()
    {
        Ejecutar(@"
            DECLARE @p TABLE (id INT); INSERT @p SELECT PK_id_persona FROM PERSONAS WHERE Dni LIKE '9857____';
            DECLARE @j TABLE (id INT); INSERT @j SELECT PK_id_jugador FROM JUGADORES WHERE FK_id_persona IN (SELECT id FROM @p);
            DECLARE @g TABLE (id INT); INSERT @g SELECT PK_id_pago FROM PAGOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DECLARE @b TABLE (id INT); INSERT @b SELECT PK_id_jugador_descuento FROM JUGADORES_DESCUENTOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DECLARE @c TABLE (id INT); INSERT @c SELECT PK_id_categoria FROM CATEGORIAS WHERE nombre_categoria LIKE 'HU057 Prueba%';
            DECLARE @a TABLE (id INT); INSERT @a SELECT PK_id_arancel FROM ARANCELES WHERE FK_id_categoria IN (SELECT id FROM @c);
            DELETE FROM PAGOS WHERE PK_id_pago IN (SELECT id FROM @g);
            DELETE FROM JUGADORES_DESCUENTOS WHERE PK_id_jugador_descuento IN (SELECT id FROM @b);
            DELETE FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM JUGADORES_PERIODOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM JUGADORES WHERE PK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM PERSONAS WHERE PK_id_persona IN (SELECT id FROM @p);
            DELETE FROM ARANCELES WHERE PK_id_arancel IN (SELECT id FROM @a);
            DELETE FROM CATEGORIAS WHERE PK_id_categoria IN (SELECT id FROM @c);
            DELETE FROM AUDITORIA_CAMBIOS WHERE
                (entidad = N'PERSONAS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @p)) OR
                (entidad = N'JUGADORES' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @j)) OR
                (entidad = N'PAGOS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @g)) OR
                (entidad = N'JUGADORES_DESCUENTOS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @b)) OR
                (entidad = N'ARANCELES' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @a)) OR
                (entidad = N'CATEGORIAS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @c));");
    }

    private static void Ejecutar(string sql, params (string Nombre, object Valor)[] parametros) => Escalar(sql, parametros);

    private static object? Escalar(string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var conexion = new SqlConnection(Conexion);
        conexion.Open();
        using var comando = new SqlCommand(sql, conexion);
        foreach (var (nombre, valor) in parametros) comando.Parameters.AddWithValue(nombre, valor);
        object? resultado = comando.ExecuteScalar();
        return resultado == DBNull.Value ? null : resultado;
    }

    private static int Contar(string sql, params (string Nombre, object Valor)[] parametros) => Convert.ToInt32(Escalar(sql, parametros));

    private static string? Texto(string sql, params (string Nombre, object Valor)[] parametros) => Escalar(sql, parametros)?.ToString()?.Trim();

    private static DateTime? Fecha(string sql, params (string Nombre, object Valor)[] parametros) => Escalar(sql, parametros) is DateTime f ? f : null;

    private static string[] Lista(string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var conexion = new SqlConnection(Conexion);
        conexion.Open();
        using var comando = new SqlCommand(sql, conexion);
        foreach (var (nombre, valor) in parametros) comando.Parameters.AddWithValue(nombre, valor);
        using var lector = comando.ExecuteReader();
        var filas = new List<string>();
        while (lector.Read()) filas.Add(lector.GetValue(0)?.ToString() ?? "");
        return filas.ToArray();
    }
}
