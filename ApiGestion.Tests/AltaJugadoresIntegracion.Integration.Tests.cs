namespace ApiGestion.Tests;

using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceLibrary;

// HU-056: el alta de jugadores y el cambio de categoría que pide el Portal Deportivo, contra la base real. El servicio, el DAO
// y el SQL corren de verdad; solo el arancel de inscripción es falso (la base de prueba no tiene) y el reloj se fija en una
// fecha de 2030 para que ni los aranceles que cargan estas pruebas ni sus cuotas se mezclen con datos reales.
// Estas pruebas COMMITEAN (la concurrencia y los rechazos así lo exigen): los DNI son del rango reservado 9810xxxx y se limpian
// al empezar y al terminar, igual que los aranceles y los rechazos que cargan.
[Collection(BaseDeDatosCollection.Nombre)]
public class AltaJugadoresIntegracionIntegrationTests : IDisposable
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static readonly DateTime Dia15 = new(2030, 3, 15);
    private static readonly DateTime Dia5 = new(2030, 3, 5);
    private const int Cat3 = 3;
    private const int Cat4 = 4;

    private readonly string _marca = "hu056-" + Guid.NewGuid().ToString("N")[..10];
    private readonly List<int> _aranceles = new();
    private int _pedidos;

    public AltaJugadoresIntegracionIntegrationTests()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable))) Limpiar();
    }

    public void Dispose()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable))) return;
        Limpiar();
        foreach (int id in _aranceles) Ejecutar("DELETE FROM ARANCELES WHERE PK_id_arancel = @a", ("@a", id));
        Ejecutar("DELETE FROM INTEGRACION_RECHAZOS WHERE correlacion LIKE @m", ("@m", _marca + "%"));
    }

    // ===== Alta: persona y jugador nuevos =====

    [IntegracionFact]
    public void UnaPersonaYUnJugadorNuevos_SeCreanConSuCategoria_YAparecenEnElPadron()
    {
        SembrarArancel("Masculino", null, 1000m);
        string dni = Dni(1);

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        var alta = resultado.Valor!;
        Assert.True(alta.Creado);
        Assert.False(alta.PersonaExistente);
        Assert.True(alta.InscripcionGenerada);
        Assert.True(alta.CuotaGenerada);

        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PERSONAS WHERE Dni = @d", ("@d", dni)));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", alta.IdJugador)));
        Assert.Equal(Cat3, Contar("SELECT FK_id_categoria FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", alta.IdJugador)));
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador = @j AND FK_id_categoria = @c",
            ("@j", alta.IdJugador), ("@c", Cat3)));
        Assert.Equal(Dia15, Fecha("SELECT fecha_alta FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", alta.IdJugador)));

        // El padrón de los listados y la búsqueda del Portal Administrativo lo ven.
        Assert.Contains(new JugadoresDao(Conexion).ListarJugadores(), j => j.IdJugador == alta.IdJugador && j.Dni == dni && j.Activo);
        Assert.Contains(new PlayerDAO(Conexion).SearchPlayersForFeeManagement(dni, 5), j => j.Id == alta.IdJugador);
    }

    [IntegracionFact]
    public void UnaPersonaExistenteSinJugador_SeReutiliza_SinPisarSusDatos_YSeCompletaSuGenero()
    {
        string dni = Dni(2);
        Ejecutar("INSERT INTO PERSONAS (nombre, apellido, Dni, fecha_de_nacimiento) VALUES (N'Previo', N'Existente', @d, '2011-01-01')", ("@d", dni));
        int persona = Contar("SELECT PK_id_persona FROM PERSONAS WHERE Dni = @d", ("@d", dni));
        SembrarArancel("Masculino", null, 1000m);

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni, nombre: "Otro", apellido: "Nombre", nacimiento: "2005-05-05"), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.True(resultado.Valor!.Creado);
        Assert.True(resultado.Valor.PersonaExistente);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM PERSONAS WHERE Dni = @d", ("@d", dni)));
        Assert.Equal(persona, Contar("SELECT FK_id_persona FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", resultado.Valor.IdJugador)));
        Assert.Equal("Previo", Texto("SELECT nombre FROM PERSONAS WHERE PK_id_persona = @p", ("@p", persona)));
        Assert.Equal(new DateTime(2011, 1, 1), Fecha("SELECT fecha_de_nacimiento FROM PERSONAS WHERE PK_id_persona = @p", ("@p", persona)));
        Assert.Equal("Masculino", Texto("SELECT genero FROM PERSONAS WHERE PK_id_persona = @p", ("@p", persona)));
    }

    [IntegracionFact]
    public void UnaPersonaExistenteNoNecesitaNombreNiNacimiento()
    {
        string dni = Dni(3);
        Ejecutar("INSERT INTO PERSONAS (nombre, apellido, Dni, genero) VALUES (N'Previo', N'Existente', @d, 'Masculino')", ("@d", dni));

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(
            new SolicitudAltaIntegracion(dni, null, null, "Masculino", null, Cat3), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.True(resultado.Valor!.PersonaExistente);
    }

    // ===== Idempotencia =====

    [IntegracionFact]
    public void UnJugadorActivoQueYaExiste_NoSeDuplica_YSeLeCompletanLosCargosQueLeFaltan()
    {
        string dni = Dni(4);
        SembrarArancel("Masculino", null, 1000m);
        // Como el alta directa del Portal Deportivo de antes: persona y jugador en la base, sin cargos.
        int jugador = CrearJugadorDirecto(dni, "Masculino", Cat3, Dia15);

        var primera = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), Correlacion());
        var segunda = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), Correlacion());

        Assert.True(primera.Exito, primera.Mensaje);
        Assert.False(primera.Valor!.Creado);
        Assert.True(primera.Valor.PersonaExistente);
        Assert.Equal(jugador, primera.Valor.IdJugador);
        Assert.True(primera.Valor.InscripcionGenerada);
        Assert.True(primera.Valor.CuotaGenerada);
        Assert.False(segunda.Valor!.Creado);
        Assert.Equal(jugador, segunda.Valor.IdJugador);
        Assert.False(segunda.Valor.InscripcionGenerada);
        Assert.False(segunda.Valor.CuotaGenerada);
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM JUGADORES WHERE FK_id_persona = (SELECT PK_id_persona FROM PERSONAS WHERE Dni = @d)", ("@d", dni)));
        Assert.Equal(2, Contar("SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @j", ("@j", jugador)));
    }

    [IntegracionFact]
    public void UnJugadorActivoQueNoEsReciente_ResponderOkSinGenerarCargos()
    {
        string dni = Dni(5);
        SembrarArancel("Masculino", null, 1000m);
        int jugador = CrearJugadorDirecto(dni, "Masculino", Cat3, Dia15.AddDays(-30));

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.False(resultado.Valor!.Creado);
        Assert.False(resultado.Valor.InscripcionGenerada);
        Assert.False(resultado.Valor.CuotaGenerada);
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @j", ("@j", jugador)));
    }

    [IntegracionFact]
    public void ElMismoPedidoTresVeces_DejaUnaPersonaUnJugadorUnaCuotaYUnaInscripcion()
    {
        string dni = Dni(6);
        SembrarArancel("Masculino", null, 1000m);
        var servicio = Servicio(Dia15);

        var respuestas = Enumerable.Range(0, 3).Select(_ => servicio.AltaDesdeIntegracion(Pedido(dni), Correlacion())).ToList();

        Assert.All(respuestas, r => Assert.True(r.Exito, r.Mensaje));
        Assert.Equal(new[] { true, false, false }, respuestas.Select(r => r.Valor!.Creado).ToArray());
        Assert.Single(respuestas.Select(r => r.Valor!.IdJugador).Distinct());
        AssertCuentas(dni, personas: 1, jugadores: 1, cuotas: 1, inscripciones: 1);
    }

    [IntegracionFact]
    public async Task DiezPedidosSimultaneosDelMismoDni_DejanUnaPersonaUnJugadorUnaCuotaYUnaInscripcion()
    {
        string dni = Dni(7);
        SembrarArancel("Masculino", null, 1000m);
        const int pedidos = 10;
        using var largada = new Barrier(pedidos);

        var tareas = Enumerable.Range(0, pedidos).Select(_ => Task.Factory.StartNew(() =>
        {
            var servicio = Servicio(Dia15);
            Assert.True(largada.SignalAndWait(TimeSpan.FromSeconds(30)), "Los pedidos no llegaron a largar juntos.");
            return servicio.AltaDesdeIntegracion(Pedido(dni), Correlacion());
        }, TaskCreationOptions.LongRunning)).ToArray();
        var respuestas = await Task.WhenAll(tareas);

        Assert.All(respuestas, r => Assert.True(r.Exito, r.Mensaje));
        Assert.Equal(1, respuestas.Count(r => r.Valor!.Creado));
        Assert.Single(respuestas.Select(r => r.Valor!.IdJugador).Distinct());
        AssertCuentas(dni, personas: 1, jugadores: 1, cuotas: 1, inscripciones: 1);
    }

    // ===== Rechazos de negocio =====

    [IntegracionFact]
    public void UnJugadorDadoDeBaja_Es409_NoSeReactivaNiSeLeGeneranCargos_YQuedaRegistrado()
    {
        string dni = Dni(8);
        SembrarArancel("Masculino", null, 1000m);
        int jugador = CrearJugadorDirecto(dni, "Masculino", Cat3, Dia15.AddDays(-60));
        Ejecutar("UPDATE JUGADORES SET fecha_baja = @f WHERE PK_id_jugador = @j", ("@f", Dia15.AddDays(-10)), ("@j", jugador));
        var antes = Instantanea(dni);
        string correlacion = Correlacion();

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), correlacion);

        Assert.False(resultado.Exito);
        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
        Assert.Contains("baja", resultado.Mensaje!);
        Assert.Equal(antes, Instantanea(dni));
        Assert.NotNull(Fecha("SELECT fecha_baja FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        var rechazo = Rechazo(correlacion);
        Assert.Equal("CONFLICT", rechazo.ErrorCode);
        Assert.Equal(jugador, rechazo.IdJugador);
        Assert.NotNull(rechazo.IdPersona);
    }

    [IntegracionFact]
    public void ElIndiceUnicoImpideUnSegundoJugadorParaLaMismaPersona()
    {
        string dni = Dni(9);
        CrearJugadorDirecto(dni, "Masculino", Cat3, Dia15);

        var error = Assert.Throws<SqlException>(() => Ejecutar(
            "INSERT INTO JUGADORES (FK_id_persona, FK_id_categoria, fecha_alta) SELECT PK_id_persona, @c, @f FROM PERSONAS WHERE Dni = @d",
            ("@c", Cat3), ("@f", Dia15), ("@d", dni)));

        Assert.True(error.Number is 2601 or 2627, $"Se esperaba una violación de unicidad y fue el error {error.Number}.");
        Assert.Equal(1, Contar("SELECT COUNT(*) FROM JUGADORES WHERE FK_id_persona = (SELECT PK_id_persona FROM PERSONAS WHERE Dni = @d)", ("@d", dni)));
    }

    [IntegracionFact]
    public void UnGeneroQueNoCoincideConElDeLaPersona_Es409_YNoSeCreaNada()
    {
        string dni = Dni(10);
        Ejecutar("INSERT INTO PERSONAS (nombre, apellido, Dni, genero) VALUES (N'Previa', N'Existente', @d, 'Femenino')", ("@d", dni));
        string correlacion = Correlacion();

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni, genero: "Masculino"), correlacion);

        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
        Assert.Contains("género no coincide", resultado.Mensaje!);
        AssertCuentas(dni, personas: 1, jugadores: 0, cuotas: 0, inscripciones: 0);
        Assert.Equal("Femenino", Texto("SELECT genero FROM PERSONAS WHERE Dni = @d", ("@d", dni)));
        Assert.Equal("CONFLICT", Rechazo(correlacion).ErrorCode);
    }

    // ===== Fecha de nacimiento: "no futura" respecto de la fecha REAL del club, no del reloj inyectado =====

    [IntegracionFact]
    public void ElNacimientoSeValidaContraLaFechaReal_AunqueElRelojDelAltaSeaAnterior()
    {
        // Reloj del alta en 2000; nacimiento en 2012: futuro para el reloj, pasado en la realidad -> se acepta.
        var resultado = Servicio(new DateTime(2000, 3, 15), inscripcionFemenina: 0m)
            .AltaDesdeIntegracion(Pedido(Dni(70), genero: "Femenino", nacimiento: "2012-05-20"), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
    }

    [IntegracionFact]
    public void UnNacimientoFuturoEnLaRealidad_SeRechaza_AunqueElRelojDelAltaSeaPosterior()
    {
        // Reloj del alta en 2030; nacimiento mañana (fecha real): pasado para el reloj, futuro en la realidad -> se rechaza.
        string manana = RelojNegocio.Hoy.AddDays(1).ToString("yyyy-MM-dd");
        string dni = Dni(71);

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni, nacimiento: manana), Correlacion());

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Equal("fechaNacimiento", resultado.Campo);
        AssertCuentas(dni, personas: 0, jugadores: 0, cuotas: 0, inscripciones: 0);
    }

    [IntegracionFact]
    public void NacerHoy_FechaReal_SeAcepta()
    {
        SembrarArancel("Masculino", null, 1000m);
        string hoy = RelojNegocio.Hoy.ToString("yyyy-MM-dd");

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(Dni(72), nacimiento: hoy), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
    }

    // ===== DNI canónico: personas históricas guardadas con espacios =====
    // La base unificada no las admite (CK_PERSONAS_dni_digitos), pero una base sin ese CHECK sí. Para sembrarlas se deshabilita
    // el CHECK solo durante el INSERT y se vuelve a habilitar WITH CHECK (confiable) en el finally; la colección no corre en
    // paralelo con nada.

    [IntegracionFact]
    public void UnaPersonaHistoricaConEspaciosEnElDni_SeReutiliza_SinDuplicarNiModificarSuDni()
    {
        SembrarArancel("Masculino", null, 1000m);
        string dni = Dni(60);
        try
        {
            int historica = PersonaConDniCrudo(" " + dni, "Masculino");

            var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), Correlacion());

            Assert.True(resultado.Exito, resultado.Mensaje);
            Assert.True(resultado.Valor!.Creado);
            Assert.True(resultado.Valor.PersonaExistente);
            Assert.Equal(1, Contar("SELECT COUNT(*) FROM PERSONAS WHERE LTRIM(RTRIM(Dni)) = @d", ("@d", dni)));
            Assert.Equal(historica, Convert.ToInt32(Escalar("SELECT FK_id_persona FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", resultado.Valor.IdJugador))));
            Assert.Equal(" " + dni, Escalar("SELECT Dni FROM PERSONAS WHERE PK_id_persona = @p", ("@p", historica)));
        }
        finally
        {
            LimpiarDniCrudo(dni);
        }
    }

    [IntegracionFact]
    public void DosPersonasConElMismoDniCanonico_SeRechazaParaRevisionManual_SinCrearNiFusionarNada()
    {
        SembrarArancel("Masculino", null, 1000m);
        string dni = Dni(61);
        try
        {
            int conEspacios = PersonaConDniCrudo(" " + dni, "Masculino");
            int exacta = PersonaConDniCrudo(dni, "Masculino");
            string correlacion = Correlacion();

            var resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), correlacion);

            Assert.False(resultado.Exito);
            Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
            Assert.Equal(2, Contar("SELECT COUNT(*) FROM PERSONAS WHERE PK_id_persona IN (@a, @b)", ("@a", conEspacios), ("@b", exacta)));
            Assert.Equal(0, Contar("SELECT COUNT(*) FROM JUGADORES WHERE FK_id_persona IN (@a, @b)", ("@a", conEspacios), ("@b", exacta)));
            Assert.Equal(" " + dni, Escalar("SELECT Dni FROM PERSONAS WHERE PK_id_persona = @p", ("@p", conEspacios)));
            Assert.Equal("CONFLICT", Rechazo(correlacion).ErrorCode);
        }
        finally
        {
            LimpiarDniCrudo(dni);
        }
    }

    [IntegracionTheory]
    [InlineData("123456", "Masculino", 3, "Prueba", "2012-05-20", 400, "dni")]
    [InlineData("123456789", "Masculino", 3, "Prueba", "2012-05-20", 400, "dni")]
    [InlineData("1234567890", "Masculino", 3, "Prueba", "2012-05-20", 400, "dni")]
    [InlineData("12.345.678", "Masculino", 3, "Prueba", "2012-05-20", 400, "dni")]
    [InlineData("", "Masculino", 3, "Prueba", "2012-05-20", 400, "dni")]
    [InlineData(null, "Masculino", 3, "Prueba", "2012-05-20", 400, "dni")]
    [InlineData("98100099", null, 3, "Prueba", "2012-05-20", 400, "genero")]
    [InlineData("98100099", "Otro", 3, "Prueba", "2012-05-20", 400, "genero")]
    [InlineData("98100099", "Masculino", null, "Prueba", "2012-05-20", 400, "idCategoria")]
    [InlineData("98100099", "Masculino", 0, "Prueba", "2012-05-20", 400, "idCategoria")]
    [InlineData("98100099", "Masculino", 999999, "Prueba", "2012-05-20", 422, "idCategoria")]
    [InlineData("98100099", "Masculino", 3, null, "2012-05-20", 400, "nombre")]
    [InlineData("98100099", "Masculino", 3, "Prueba", null, 400, "fechaNacimiento")]
    [InlineData("98100099", "Masculino", 3, "Prueba", "20/05/2012", 400, "fechaNacimiento")]
    [InlineData("98100099", "Masculino", 3, "Prueba", "2999-01-01", 400, "fechaNacimiento")]
    public void UnPedidoInvalido_NoCreaNada_YQuedaRegistradoSinElDni(
        string? dni, string? genero, int? categoria, string? nombre, string? nacimiento, int estadoEsperado, string campo)
    {
        int personasAntes = Contar("SELECT COUNT(*) FROM PERSONAS");
        int jugadoresAntes = Contar("SELECT COUNT(*) FROM JUGADORES");
        string correlacion = Correlacion();

        var resultado = Servicio(Dia15).AltaDesdeIntegracion(
            new SolicitudAltaIntegracion(dni, nombre, nombre == null ? null : "Apellido", genero, nacimiento, categoria), correlacion);

        Assert.False(resultado.Exito);
        Assert.Equal(estadoEsperado == 400 ? TipoResultado.Invalido : TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Equal(campo, resultado.Campo);
        Assert.Equal(personasAntes, Contar("SELECT COUNT(*) FROM PERSONAS"));
        Assert.Equal(jugadoresAntes, Contar("SELECT COUNT(*) FROM JUGADORES"));

        var rechazo = Rechazo(correlacion);
        Assert.Equal(estadoEsperado == 400 ? "INVALID_INPUT" : "UNPROCESSABLE", rechazo.ErrorCode);
        Assert.Equal("AltaJugador", rechazo.Operacion);
        if (!string.IsNullOrEmpty(dni)) Assert.DoesNotContain(dni, rechazo.Motivo);
        Assert.DoesNotContain("98100099", rechazo.Motivo);
    }

    // ===== Cargos: las mismas reglas que el alta del Portal Administrativo =====

    [IntegracionFact]
    public void AntesDelDia10_SeGeneraLaInscripcion_PeroNoLaCuota()
    {
        string dni = Dni(11);
        SembrarArancel("Masculino", null, 1000m);

        var resultado = Servicio(Dia5).AltaDesdeIntegracion(Pedido(dni), Correlacion());

        Assert.True(resultado.Valor!.InscripcionGenerada);
        Assert.False(resultado.Valor.CuotaGenerada);
        AssertCuentas(dni, personas: 1, jugadores: 1, cuotas: 0, inscripciones: 1);
    }

    [IntegracionFact]
    public void DesdeElDia10_LaCuotaTomaElArancelDeLaCategoria_ConPrioridadSobreElDelGenero()
    {
        string conArancelDeCategoria = Dni(12);
        string soloPorGenero = Dni(13);
        SembrarArancel("Masculino", null, 1000m);
        SembrarArancel(null, Cat4, 2000m);

        var a = Servicio(Dia15).AltaDesdeIntegracion(Pedido(conArancelDeCategoria, cat: Cat4), Correlacion());
        var b = Servicio(Dia15).AltaDesdeIntegracion(Pedido(soloPorGenero, cat: Cat3), Correlacion());

        Assert.True(a.Valor!.CuotaGenerada);
        Assert.True(b.Valor!.CuotaGenerada);
        Assert.Equal(2000m, MontoDeCuota(a.Valor.IdJugador));
        Assert.Equal(1000m, MontoDeCuota(b.Valor.IdJugador));
        Assert.Equal(new DateTime(2030, 3, 1), Fecha("SELECT fecha_vencimiento FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Cuota'", ("@j", a.Valor.IdJugador)));
        Assert.Equal(Dia15, Fecha("SELECT fecha_vencimiento FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Inscripcion'", ("@j", a.Valor.IdJugador)));
    }

    [IntegracionFact]
    public void SinArancelVigente_NoSeGeneraLaCuota_NiSeGrabaNingunCargoEnCero()
    {
        string dni = Dni(14);
        // 2000: ningún arancel está vigente. Inscripción femenina en $0: tampoco se cobra.
        var hoy = new DateTime(2000, 3, 15);

        var resultado = Servicio(hoy, inscripcionFemenina: 0m).AltaDesdeIntegracion(Pedido(dni, genero: "Femenino"), Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.True(resultado.Valor!.Creado);
        Assert.False(resultado.Valor.InscripcionGenerada);
        Assert.False(resultado.Valor.CuotaGenerada);
        AssertCuentas(dni, personas: 1, jugadores: 1, cuotas: 0, inscripciones: 0);
        Assert.Equal(0, Contar("SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @j AND monto_base <= 0", ("@j", resultado.Valor.IdJugador)));
    }

    [IntegracionFact]
    public void UnVaronSinArancelDeInscripcion_Es422_YSeRevierteTodo()
    {
        string dni = Dni(15);
        SembrarArancel("Masculino", null, 1000m);
        string correlacion = Correlacion();
        int auditoriaAntes = Contar("SELECT COUNT(*) FROM AUDITORIA_CAMBIOS");

        var resultado = Servicio(Dia15, inscripcionMasculina: null).AltaDesdeIntegracion(Pedido(dni), correlacion);

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("arancel de inscripción", resultado.Mensaje!);
        AssertCuentas(dni, personas: 0, jugadores: 0, cuotas: 0, inscripciones: 0);
        // Una transacción revertida no deja ni sus eventos de auditoría.
        Assert.Equal(auditoriaAntes, Contar("SELECT COUNT(*) FROM AUDITORIA_CAMBIOS"));
        Assert.Equal("UNPROCESSABLE", Rechazo(correlacion).ErrorCode);
    }

    // ===== Auditoría =====

    [IntegracionFact]
    public void ElAlta_QuedaAuditadaConElDeportivoComoOrigen_YElOperadorDelToken()
    {
        string dni = Dni(16);
        SembrarArancel("Masculino", null, 1000m);
        int operador = Contar("SELECT TOP (1) PK_id_usuario FROM USUARIOS ORDER BY PK_id_usuario");

        ResultadoOperacion<AltaIntegracionRegistrada> resultado;
        using (SqlAuditActorContext.Push(operador))
        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            resultado = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni), Correlacion());
        }

        Assert.True(resultado.Exito, resultado.Mensaje);
        int persona = Contar("SELECT PK_id_persona FROM PERSONAS WHERE Dni = @d", ("@d", dni));
        int pago = Contar("SELECT TOP (1) PK_id_pago FROM PAGOS WHERE FK_id_jugador = @j", ("@j", resultado.Valor!.IdJugador));
        foreach (var (entidad, id) in new[] { ("PERSONAS", persona), ("JUGADORES", resultado.Valor.IdJugador), ("PAGOS", pago) })
        {
            Assert.Equal(1, Contar(
                "SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = @e AND id_entidad = @i AND origen_sistema = N'PortalDeportivo' AND id_usuario = @u",
                ("@e", entidad), ("@i", id.ToString()), ("@u", operador)));
        }
    }

    // ===== Cambio de categoría =====

    [IntegracionFact]
    public void ElCambioDeCategoria_ActualizaLaDelJugador_SinTocarNingunPago_YLasProximasEmisionesUsanLaNueva()
    {
        string dni = Dni(17);
        SembrarArancel("Masculino", null, 1000m);
        SembrarArancel(null, Cat4, 2000m);
        var alta = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni, cat: Cat3), Correlacion()).Valor!;
        string pagosAntes = Pagos(alta.IdJugador);
        Assert.NotEqual("", pagosAntes);
        Assert.Equal(1000m, new ArancelesDao(Conexion).ObtenerMontoVigente("Masculino", Cat3, Dia15));

        var resultado = Servicio(Dia15).CambiarCategoria(alta.IdJugador, Cat4, Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.True(resultado.Valor!.Cambiado);
        Assert.Equal(Cat4, resultado.Valor.IdCategoria);
        Assert.Equal(Cat4, Contar("SELECT FK_id_categoria FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", alta.IdJugador)));
        // Las filas N:M del Deportivo no se tocan: la anterior sigue y se suma la nueva.
        Assert.Equal(2, Contar("SELECT COUNT(*) FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador = @j", ("@j", alta.IdJugador)));
        // Lo ya emitido conserva su monto; solo las próximas emisiones usan la categoría nueva.
        Assert.Equal(pagosAntes, Pagos(alta.IdJugador));
        Assert.Equal(2000m, new ArancelesDao(Conexion).ObtenerMontoVigente("Masculino", Cat4, new DateTime(2030, 4, 30)));
    }

    [IntegracionFact]
    public void LaMismaCategoria_EsOkSinCambios()
    {
        string dni = Dni(18);
        SembrarArancel("Masculino", null, 1000m);
        var alta = Servicio(Dia15).AltaDesdeIntegracion(Pedido(dni, cat: Cat3), Correlacion()).Valor!;
        int auditoriaAntes = Contar("SELECT COUNT(*) FROM AUDITORIA_CAMBIOS");

        var resultado = Servicio(Dia15).CambiarCategoria(alta.IdJugador, Cat3, Correlacion());

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.False(resultado.Valor!.Cambiado);
        Assert.Equal(auditoriaAntes, Contar("SELECT COUNT(*) FROM AUDITORIA_CAMBIOS"));
    }

    [IntegracionFact]
    public void ElCambioDeCategoriaDeUnJugadorDadoDeBaja_Es422_YQuedaRegistradoConSuId()
    {
        string dni = Dni(19);
        int jugador = CrearJugadorDirecto(dni, "Masculino", Cat3, Dia15.AddDays(-60));
        Ejecutar("UPDATE JUGADORES SET fecha_baja = @f WHERE PK_id_jugador = @j", ("@f", Dia15.AddDays(-10)), ("@j", jugador));
        string correlacion = Correlacion();

        var resultado = Servicio(Dia15).CambiarCategoria(jugador, Cat4, correlacion);

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Equal(Cat3, Contar("SELECT FK_id_categoria FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        var rechazo = Rechazo(correlacion);
        Assert.Equal("CambioCategoria", rechazo.Operacion);
        Assert.Equal(jugador, rechazo.IdJugador);
    }

    [IntegracionTheory]
    [InlineData(999999999, 4, TipoResultado.NoEncontrado, null)]
    [InlineData(0, 4, TipoResultado.Invalido, "idJugador")]
    [InlineData(-3, 4, TipoResultado.Invalido, "idJugador")]
    public void ElCambioDeCategoriaConUnJugadorInvalidoOInexistente_SeRechazaYSeRegistra(
        int idJugador, int categoria, TipoResultado esperado, string? campo)
    {
        string correlacion = Correlacion();

        var resultado = Servicio(Dia15).CambiarCategoria(idJugador, categoria, correlacion);

        Assert.Equal(esperado, resultado.Tipo);
        Assert.Equal(campo, resultado.Campo);
        Assert.Equal("CambioCategoria", Rechazo(correlacion).Operacion);
    }

    [IntegracionTheory]
    [InlineData(null, TipoResultado.Invalido)]
    [InlineData(0, TipoResultado.Invalido)]
    [InlineData(999999, TipoResultado.NoProcesable)]
    public void ElCambioDeCategoriaConUnaCategoriaInvalida_SeRechazaConElCampoIdCategoria(int? categoria, TipoResultado esperado)
    {
        var jugador = CrearJugadorDirecto(Dni(20), "Masculino", Cat3, Dia15);
        string correlacion = Correlacion();

        var resultado = Servicio(Dia15).CambiarCategoria(jugador, categoria, correlacion);

        Assert.Equal(esperado, resultado.Tipo);
        Assert.Equal("idCategoria", resultado.Campo);
        Assert.Equal(Cat3, Contar("SELECT FK_id_categoria FROM JUGADORES WHERE PK_id_jugador = @j", ("@j", jugador)));
        Assert.NotNull(Rechazo(correlacion));
    }

    // ===== El registro de rechazos =====

    [IntegracionFact]
    public void ElRegistroDeRechazos_GuardaElOperadorYElOrigenDelPedido_SinDatosPersonales()
    {
        int operador = Contar("SELECT TOP (1) PK_id_usuario FROM USUARIOS ORDER BY PK_id_usuario");
        string correlacion = Correlacion();

        using (SqlAuditActorContext.Push(operador))
        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            new IntegracionRechazosDao(Conexion).Registrar(
                new RechazoIntegracion(OperacionesDeIntegracion.AltaJugador, "CONFLICT", "Motivo de prueba.", correlacion, null, null));
        }

        var rechazo = Rechazo(correlacion);
        Assert.Equal("PortalDeportivo", rechazo.Origen);
        Assert.Equal(operador, rechazo.IdUsuario);
        Assert.Equal("AltaJugador", rechazo.Operacion);
        Assert.Equal("Motivo de prueba.", rechazo.Motivo);
        string[] columnas = Lista("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.INTEGRACION_RECHAZOS')");
        Assert.DoesNotContain(columnas, c => c.Contains("dni", StringComparison.OrdinalIgnoreCase) || c.Contains("nombre", StringComparison.OrdinalIgnoreCase));
    }

    // ===== Infraestructura de las pruebas =====

    private JugadoresAltaService Servicio(DateTime hoy, decimal? inscripcionMasculina = 70_000m, decimal? inscripcionFemenina = 0m)
        => new(new PlayerDAO(Conexion), new CategoriasDao(Conexion), new InscripcionFalsa(inscripcionMasculina, inscripcionFemenina),
            new ArancelesDao(Conexion), () => hoy,
            new RegistroRechazosIntegracion(new IntegracionRechazosDao(Conexion), NullLogger<RegistroRechazosIntegracion>.Instance));

    private static string Dni(int n) => (98100000 + n).ToString();

    private string Correlacion() => $"{_marca}-{Interlocked.Increment(ref _pedidos)}";

    private static SolicitudAltaIntegracion Pedido(string dni, string genero = "Masculino", int cat = Cat3,
        string nombre = "Prueba", string apellido = "Integracion", string nacimiento = "2012-05-20")
        => new(dni, nombre, apellido, genero, nacimiento, cat);

    private void SembrarArancel(string? genero, int? categoria, decimal monto)
    {
        using var conexion = new SqlConnection(Conexion);
        conexion.Open();
        using var comando = new SqlCommand(
            "INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (@g, @c, @m, '2030-01-01'); SELECT CAST(SCOPE_IDENTITY() AS INT);", conexion);
        comando.Parameters.AddWithValue("@g", (object?)genero ?? DBNull.Value);
        comando.Parameters.AddWithValue("@c", (object?)categoria ?? DBNull.Value);
        comando.Parameters.AddWithValue("@m", monto);
        _aranceles.Add(Convert.ToInt32(comando.ExecuteScalar()));
    }

    // Persona y jugador directo en la base, sin cargos: lo que dejaba el alta del Portal Deportivo antes de HU-056.
    private static int CrearJugadorDirecto(string dni, string genero, int categoria, DateTime alta)
    {
        Ejecutar("INSERT INTO PERSONAS (nombre, apellido, Dni, genero, fecha_de_nacimiento) VALUES (N'Directo', N'Deportivo', @d, @g, '2011-01-01')",
            ("@d", dni), ("@g", genero));
        Ejecutar(@"INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria, fecha_alta)
                   SELECT NEXT VALUE FOR dbo.SEQ_JUGADORES, PK_id_persona, @c, @f FROM PERSONAS WHERE Dni = @d",
            ("@c", categoria), ("@f", alta), ("@d", dni));
        return Contar("SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d", ("@d", dni));
    }

    private void AssertCuentas(string dni, int personas, int jugadores, int cuotas, int inscripciones)
    {
        Assert.Equal(personas, Contar("SELECT COUNT(*) FROM PERSONAS WHERE Dni = @d", ("@d", dni)));
        Assert.Equal(jugadores, Contar("SELECT COUNT(*) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d", ("@d", dni)));
        Assert.Equal(cuotas, Contar(
            "SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d AND g.concepto = 'Cuota'",
            ("@d", dni)));
        Assert.Equal(inscripciones, Contar(
            "SELECT COUNT(*) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d AND g.concepto = 'Inscripcion'",
            ("@d", dni)));
    }

    // Todo lo que cuelga de la persona, para comprobar que un rechazo no cambió nada.
    private static string Instantanea(string dni) => string.Join("|", Lista(@"
        SELECT CONCAT('P', p.PK_id_persona, p.genero) FROM PERSONAS p WHERE p.Dni = @d
        UNION ALL SELECT CONCAT('J', j.PK_id_jugador, '-', j.FK_id_categoria, '-', j.fecha_baja) FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d
        UNION ALL SELECT CONCAT('G', g.PK_id_pago, g.monto_final) FROM PAGOS g JOIN JUGADORES j ON j.PK_id_jugador = g.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d
        UNION ALL SELECT CONCAT('C', c.FK_id_categoria) FROM JUGADORES_CATEGORIAS c JOIN JUGADORES j ON j.PK_id_jugador = c.FK_id_jugador JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona WHERE p.Dni = @d",
        ("@d", dni)).OrderBy(x => x));

    private static string Pagos(int jugador) => string.Join("|", Lista(
        "SELECT CONCAT(PK_id_pago, ':', concepto, ':', monto_base, ':', monto_final, ':', estado) FROM PAGOS WHERE FK_id_jugador = @j ORDER BY PK_id_pago",
        ("@j", jugador)));

    private static decimal MontoDeCuota(int jugador) => Convert.ToDecimal(Escalar("SELECT monto_base FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Cuota'", ("@j", jugador)));

    private sealed record RechazoGuardado(string Operacion, string ErrorCode, string Motivo, string? Origen, int? IdUsuario, int? IdPersona, int? IdJugador);

    private static RechazoGuardado Rechazo(string correlacion)
    {
        using var conexion = new SqlConnection(Conexion);
        conexion.Open();
        using var comando = new SqlCommand(
            "SELECT operacion, error_code, motivo, origen_sistema, id_usuario, id_persona, id_jugador FROM INTEGRACION_RECHAZOS WHERE correlacion = @c", conexion);
        comando.Parameters.AddWithValue("@c", correlacion);
        using var lector = comando.ExecuteReader();
        Assert.True(lector.Read(), "El rechazo tiene que quedar registrado.");
        var rechazo = new RechazoGuardado(lector.GetString(0), lector.GetString(1), lector.GetString(2),
            lector.IsDBNull(3) ? null : lector.GetString(3), lector.IsDBNull(4) ? null : lector.GetInt32(4),
            lector.IsDBNull(5) ? null : lector.GetInt32(5), lector.IsDBNull(6) ? null : lector.GetInt32(6));
        Assert.False(lector.Read(), "Un solo rechazo por pedido.");
        return rechazo;
    }

    // Una persona con el DNI tal cual (con espacios), como las que podía guardar una base sin CK_PERSONAS_dni_digitos. El CHECK
    // queda deshabilitado hasta LimpiarDniCrudo: no se puede volver a validar mientras esa fila exista.
    private static int PersonaConDniCrudo(string dniCrudo, string genero)
    {
        Ejecutar("ALTER TABLE PERSONAS NOCHECK CONSTRAINT CK_PERSONAS_dni_digitos");
        return Convert.ToInt32(Escalar(@"
            INSERT INTO PERSONAS (nombre, apellido, dni, genero, fecha_de_nacimiento)
            VALUES (N'Historica', N'Con Espacios', @d, @g, '2011-01-01');
            SELECT CAST(SCOPE_IDENTITY() AS INT);",
            ("@d", dniCrudo), ("@g", genero)));
    }

    // Borra lo sembrado con PersonaConDniCrudo (y lo que el alta haya creado) y vuelve a habilitar el CHECK WITH CHECK, así
    // queda confiable como antes. Si la limpieza fallara, el ALTER también falla y la prueba lo muestra.
    private static void LimpiarDniCrudo(string dni)
    {
        Ejecutar(@"
            DECLARE @p TABLE (id INT); INSERT @p SELECT PK_id_persona FROM PERSONAS WHERE LTRIM(RTRIM(Dni)) = @d;
            DECLARE @j TABLE (id INT); INSERT @j SELECT PK_id_jugador FROM JUGADORES WHERE FK_id_persona IN (SELECT id FROM @p);
            DECLARE @g TABLE (id INT); INSERT @g SELECT PK_id_pago FROM PAGOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM PAGOS WHERE PK_id_pago IN (SELECT id FROM @g);
            DELETE FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM JUGADORES_PERIODOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM JUGADORES WHERE PK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM PERSONAS WHERE PK_id_persona IN (SELECT id FROM @p);
            DELETE FROM AUDITORIA_CAMBIOS WHERE
                (entidad = N'PERSONAS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @p)) OR
                (entidad = N'JUGADORES' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @j)) OR
                (entidad = N'PAGOS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @g));", ("@d", dni));
        Ejecutar("ALTER TABLE PERSONAS WITH CHECK CHECK CONSTRAINT CK_PERSONAS_dni_digitos");
    }

    private static void Limpiar()
    {
        Ejecutar(@"
            DECLARE @p TABLE (id INT); INSERT @p SELECT PK_id_persona FROM PERSONAS WHERE Dni LIKE '9810____';
            DECLARE @j TABLE (id INT); INSERT @j SELECT PK_id_jugador FROM JUGADORES WHERE FK_id_persona IN (SELECT id FROM @p);
            DECLARE @g TABLE (id INT); INSERT @g SELECT PK_id_pago FROM PAGOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM PAGOS WHERE PK_id_pago IN (SELECT id FROM @g);
            DELETE FROM JUGADORES_CATEGORIAS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM JUGADORES_PERIODOS WHERE FK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM JUGADORES WHERE PK_id_jugador IN (SELECT id FROM @j);
            DELETE FROM PERSONAS WHERE PK_id_persona IN (SELECT id FROM @p);
            DELETE FROM AUDITORIA_CAMBIOS WHERE
                (entidad = N'PERSONAS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @p)) OR
                (entidad = N'JUGADORES' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @j)) OR
                (entidad = N'PAGOS' AND id_entidad IN (SELECT CAST(id AS VARCHAR(20)) FROM @g));");
    }

    private static void Ejecutar(string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var conexion = new SqlConnection(Conexion);
        conexion.Open();
        using var comando = new SqlCommand(sql, conexion);
        foreach (var (nombre, valor) in parametros) comando.Parameters.AddWithValue(nombre, valor);
        comando.ExecuteNonQuery();
    }

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

    private sealed class InscripcionFalsa : EnrollmentFeeDAO
    {
        private readonly decimal? _masculina;
        private readonly decimal? _femenina;

        public InscripcionFalsa(decimal? masculina, decimal? femenina) : base("")
        {
            _masculina = masculina;
            _femenina = femenina;
        }

        public override EnrollmentFee? GetCurrentEnrollmentFee(string gender)
        {
            decimal? monto = gender == "Femenino" ? _femenina : _masculina;
            return monto == null ? null : new EnrollmentFee { Gender = gender, Amount = monto.Value };
        }
    }
}
