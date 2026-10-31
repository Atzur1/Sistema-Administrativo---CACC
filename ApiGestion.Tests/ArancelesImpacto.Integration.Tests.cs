namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-106 contra la base real, dentro de una transacción que se revierte: la consulta de impacto no escribe nada, respeta
// la jerarquía de aranceles (HU-048) y la elegibilidad del generador, y lo que simula es exactamente lo que después emite
// el generador de cuotas. Las cuotas de meses anteriores no se tocan y la auditoría guarda el impacto aprobado.
// Cada prueba usa categorías propias, así el resultado no depende de los datos que ya tenga la base.
[Collection(BaseDeDatosCollection.Nombre)]
public class ArancelesImpactoIntegrationTests
{
    private static readonly DateTime Hoy = RelojNegocio.Hoy;
    // El mes siguiente: el arancel nuevo todavía no emitió nada y el generador lo emite recién al llegar ese mes.
    private static readonly DateTime Mes = new DateTime(Hoy.Year, Hoy.Month, 1).AddMonths(1);
    private static readonly DateTime FinDeMes = Mes.AddMonths(1).AddDays(-1);

    private readonly ArancelesDao _aranceles = new("sin uso: cada prueba pasa su conexión");
    private readonly PagosDao _pagos = new("sin uso: cada prueba pasa su conexión");
    private int _dni = 98770000 + Random.Shared.Next(0, 9000);

    private static SqlConnection Abrir()
    {
        var conexion = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        conexion.Open();
        return conexion;
    }

    private static object Escalar(SqlConnection c, SqlTransaction t, string sql, params (string, object)[] parametros)
    {
        using var cmd = new SqlCommand(sql, c, t);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        return cmd.ExecuteScalar()!;
    }

    private static int Categoria(SqlConnection c, SqlTransaction t, string nombre) => Convert.ToInt32(Escalar(c, t,
        "DECLARE @n TABLE (id INT); INSERT INTO CATEGORIAS (nombre_categoria) OUTPUT INSERTED.PK_id_categoria INTO @n VALUES (@nombre); SELECT id FROM @n;",
        ("@nombre", nombre)));

    private int Jugador(SqlConnection c, SqlTransaction t, string genero, int idCategoria, DateTime? baja = null)
    {
        var jugador = new Player
        {
            FirstName = "Impacto", LastName = "HU106", Dni = (++_dni).ToString(), BirthDate = new DateTime(2011, 1, 1),
            Gender = genero, CategoryId = idCategoria, JoinDate = Hoy.AddMonths(-3)
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, jugador), jugador);
        if (baja != null) Escalar(c, t, "UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @id; SELECT 1", ("@b", baja.Value), ("@id", id));
        return id;
    }

    private static decimal? CuotaDelMes(SqlConnection c, SqlTransaction t, int idJugador, DateTime mes)
    {
        var valor = Escalar(c, t, @"SELECT MAX(monto_base) FROM PAGOS WHERE FK_id_jugador = @id AND concepto = 'Cuota'
                                     AND fecha_vencimiento >= @mes AND fecha_vencimiento < DATEADD(MONTH, 1, @mes)", ("@id", idJugador), ("@mes", mes));
        return valor == DBNull.Value ? null : Convert.ToDecimal(valor);
    }

    private static string Conteos(SqlConnection c, SqlTransaction t) => Convert.ToString(Escalar(c, t,
        "SELECT CONCAT((SELECT COUNT(*) FROM ARANCELES), '|', (SELECT COUNT(*) FROM PAGOS), '|', (SELECT COUNT(*) FROM AUDITORIA_CAMBIOS), '|', (SELECT CHECKSUM_AGG(CHECKSUM(*)) FROM PAGOS))"))!;

    [IntegracionFact]
    public void LaSimulacionNoEscribeNada()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        int categoria = Categoria(c, t, "HU106 sin escritura");
        Jugador(c, t, "Masculino", categoria);
        string antes = Conteos(c, t);

        _aranceles.ObtenerMontoVigenteDelDestino(c, t, "Masculino", null, Hoy);
        _aranceles.ObtenerVigenteDesdeEnMes(c, t, "Masculino", null, Mes.Year, Mes.Month);
        _aranceles.ObtenerSiguienteVigenteDesde(c, t, "Masculino", null, Mes);
        _aranceles.ConsultarImpacto(c, t, "Masculino", null, 123456, FinDeMes);
        _aranceles.ConsultarImpacto(c, t, null, categoria, 50000, FinDeMes);
        _pagos.ContarCuotasConPagos(c, t, "Masculino", null, Mes, Mes.AddMonths(1), null);
        _pagos.ContarCuotasPendientesSinPagos(c, t, "Masculino", null, Mes, Mes.AddMonths(1));

        Assert.Equal(antes, Conteos(c, t)); // ARANCELES, PAGOS, AUDITORIA_CAMBIOS y el contenido de PAGOS, idénticos
        t.Rollback();
    }

    // Un arancel de género no alcanza a quien tiene arancel de categoría (HU-048) ni a quien ya se fue, y lo que se
    // simula es exactamente lo que después emite el generador de cuotas con el arancel grabado.
    [IntegracionFact]
    public void ArancelDeGenero_RespetaLaJerarquiaYCoincideConLasCuotasQueSeEmiten()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        int conArancelPropio = Categoria(c, t, "HU106 con arancel propio");
        int sinArancelPropio = Categoria(c, t, "HU106 sin arancel propio");
        _aranceles.ProgramarArancel(c, t, null, conArancelPropio, 70000, Hoy);
        // El arancel masculino que rige hoy (si la base ya tiene uno más nuevo dentro del mes, gana ese: se lee abajo).
        _aranceles.ProgramarArancel(c, t, "Masculino", null, 60000, Hoy);
        int a1 = Jugador(c, t, "Masculino", conArancelPropio), a2 = Jugador(c, t, "Masculino", conArancelPropio);
        int b1 = Jugador(c, t, "Masculino", sinArancelPropio), b2 = Jugador(c, t, "Masculino", sinArancelPropio), b3 = Jugador(c, t, "Masculino", sinArancelPropio);
        int deBaja = Jugador(c, t, "Masculino", sinArancelPropio, baja: Hoy);              // se fue antes de que empiece el mes
        int jugadora = Jugador(c, t, "Femenino", sinArancelPropio);                         // otro género: fuera del alcance
        decimal? masculinoHoy = MasculinoParaElMes(c, t);

        ImpactoArancelConsulta impacto = _aranceles.ConsultarImpacto(c, t, "Masculino", null, 123456, FinDeMes);

        var linea = Assert.Single(impacto.Lineas, l => l.IdCategoria == sinArancelPropio);
        Assert.Equal(3, linea.Jugadores);                       // b1, b2, b3: ni el de baja ni la jugadora
        Assert.Equal("Masculino", linea.Genero);
        Assert.NotNull(masculinoHoy);
        Assert.Equal(masculinoHoy, linea.MontoActual);          // lo que pagarían hoy ese mes: el arancel masculino
        Assert.Equal((123456 - masculinoHoy!.Value) * 3, linea.DiferenciaTotal);
        Assert.Equal(123456, linea.MontoNuevo);
        Assert.DoesNotContain(impacto.Lineas, l => l.IdCategoria == conArancelPropio);
        Assert.Equal(2, Assert.Single(impacto.Excluidos, e => e.IdCategoria == conArancelPropio).Jugadores);

        // Lo real: se graba el arancel y el generador emite el mes con el código de siempre.
        _aranceles.ProgramarArancel(c, t, "Masculino", null, 123456, FinDeMes);
        _pagos.GenerarCuotasPendientesDelMes(c, t, "Masculino", null, Mes.Month, Mes.Year);

        Assert.All(new[] { b1, b2, b3 }, id => Assert.Equal(linea.MontoNuevo, CuotaDelMes(c, t, id, Mes)));
        Assert.All(new[] { a1, a2 }, id => Assert.Equal(70000, CuotaDelMes(c, t, id, Mes)));
        Assert.Null(CuotaDelMes(c, t, deBaja, Mes));
        Assert.Null(CuotaDelMes(c, t, jugadora, Mes));
        t.Rollback();
    }

    // Arancel de una categoría: alcanza a todos sus jugadores, de los dos géneros, cada uno con su monto actual.
    [IntegracionFact]
    public void ArancelDeCategoria_AlcanzaALosDosGenerosConSuMontoActual()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        int categoria = Categoria(c, t, "HU106 mixta");
        int varon = Jugador(c, t, "Masculino", categoria);
        int mujer = Jugador(c, t, "Femenino", categoria);

        ImpactoArancelConsulta impacto = _aranceles.ConsultarImpacto(c, t, null, categoria, 50000, FinDeMes);

        Assert.Equal(new[] { "Femenino", "Masculino" }, impacto.Lineas.Select(l => l.Genero).OrderBy(g => g));
        Assert.All(impacto.Lineas, l => Assert.Equal(1, l.Jugadores));
        Assert.Empty(impacto.Excluidos);

        _aranceles.ProgramarArancel(c, t, null, categoria, 50000, FinDeMes);
        _pagos.GenerarCuotasPendientesDelMes(c, t, null, categoria, Mes.Month, Mes.Year);
        Assert.Equal(50000, CuotaDelMes(c, t, varon, Mes));
        Assert.Equal(50000, CuotaDelMes(c, t, mujer, Mes));
        t.Rollback();
    }

    // Una cuota ya emitida conserva su monto: programar un arancel nuevo no la recalcula.
    [IntegracionFact]
    public void LasCuotasYaEmitidasConservanSuMonto()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        int categoria = Categoria(c, t, "HU106 historica");
        int jugador = Jugador(c, t, "Masculino", categoria);
        DateTime mesAnterior = new DateTime(Hoy.Year, Hoy.Month, 1).AddMonths(-1);
        Escalar(c, t, @"INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto)
                        VALUES (@id, 777, 777, @mes, 0, 'Cuota'), (@id, 888, 888, @mesSiguiente, 0, 'Cuota'); SELECT 1",
            ("@id", jugador), ("@mes", mesAnterior), ("@mesSiguiente", Mes));

        _aranceles.ProgramarArancel(c, t, null, categoria, 99999, Mes);
        _pagos.GenerarCuotasPendientesDelMes(c, t, null, categoria, Mes.Month, Mes.Year);

        Assert.Equal(777, CuotaDelMes(c, t, jugador, mesAnterior)); // el mes anterior queda como se emitió
        Assert.Equal(888, CuotaDelMes(c, t, jugador, Mes));         // la del mes ya emitida tampoco se pisa
        t.Rollback();
    }

    // El conteo que muestra la simulación es exactamente lo que quita la confirmación del mes en curso.
    [IntegracionFact]
    public void ContarPendientesSinPagos_EsLoQueQuitaLaConfirmacion()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        int categoria = Categoria(c, t, "HU106 mes en curso");
        int pendiente = Jugador(c, t, "Masculino", categoria), conPago = Jugador(c, t, "Masculino", categoria);
        DateTime mesActual = new DateTime(Hoy.Year, Hoy.Month, 1);
        Escalar(c, t, @"INSERT PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto)
                        VALUES (@p, 1000, 1000, @mes, 0, 'Cuota'), (@c, 1000, 600, @mes, 0, 'Cuota'), (@c, 1000, 400, @mes, 1, 'Cuota'); SELECT 1",
            ("@p", pendiente), ("@c", conPago), ("@mes", mesActual));

        int pendientes = _pagos.ContarCuotasPendientesSinPagos(c, t, null, categoria, mesActual, mesActual.AddMonths(1));
        int conPagos = _pagos.ContarCuotasConPagos(c, t, null, categoria, mesActual, mesActual.AddMonths(1), null);
        int quitadas = _pagos.EliminarCuotasPendientesSinPagos(c, t, null, categoria, mesActual, mesActual.AddMonths(1), null);

        Assert.Equal(1, pendientes);
        Assert.Equal(1, conPagos);
        Assert.Equal(pendientes, quitadas);
        Assert.Equal(1000, CuotaDelMes(c, t, conPago, mesActual)); // la que tenía un abono no se toca
        t.Rollback();
    }

    // La auditoría del arancel guarda lo que se aprobó: tipo, valor, monto anterior, jugadores, diferencia y desglose.
    [IntegracionFact]
    public void LaAuditoriaGuardaElImpactoAprobado()
    {
        using var c = Abrir();
        using var t = c.BeginTransaction();
        int categoria = Categoria(c, t, "HU106 auditoria");
        var impacto = new ImpactoArancel
        {
            Tipo = TipoCambioArancel.Porcentaje, Valor = 10, MontoBase = 85000, MontoNuevo = 93500,
            Lineas = new() { new ImpactoArancelLinea { IdCategoria = categoria, NombreCategoria = "HU106 auditoria", Genero = "Masculino", MontoActual = 85000, MontoNuevo = 93500, Jugadores = 42 } }
        };

        _aranceles.ProgramarArancel(c, t, null, categoria, 93500, Mes, impacto);

        string evento = Convert.ToString(Escalar(c, t, @"
            SELECT TOP (1) CONCAT(JSON_VALUE(datos_despues, '$.tipoCambio'), '|', JSON_VALUE(datos_despues, '$.valorCambio'), '|',
                   JSON_VALUE(datos_despues, '$.montoAnterior'), '|', JSON_VALUE(datos_despues, '$.monto'), '|',
                   JSON_VALUE(datos_despues, '$.jugadoresAlcanzados'), '|', JSON_VALUE(datos_despues, '$.diferenciaMensual'), '|',
                   JSON_VALUE(datos_despues, '$.impacto[0].jugadores'), '|', accion)
            FROM AUDITORIA_CAMBIOS WHERE entidad = N'ARANCELES' AND id_entidad = CONVERT(NVARCHAR(128),
                 (SELECT MAX(PK_id_arancel) FROM ARANCELES WHERE FK_id_categoria = @cat))
            ORDER BY PK_id_evento DESC", ("@cat", categoria)))!;

        Assert.Equal("Porcentaje|10.00|85000.00|93500.00|42|357000.00|42|INSERT", evento);
        t.Rollback();
    }

    // Lo que hoy le correspondería a un varón sin arancel de categoría ese mes (el arancel masculino), leído con la misma
    // regla que el generador.
    private static decimal? MasculinoParaElMes(SqlConnection c, SqlTransaction t)
    {
        var valor = Escalar(c, t, @"SELECT TOP (1) monto FROM ARANCELES WHERE genero = 'Masculino' AND FK_id_categoria IS NULL
                                     AND vigente_desde <= @fin ORDER BY vigente_desde DESC", ("@fin", FinDeMes));
        return valor == null || valor == DBNull.Value ? null : Convert.ToDecimal(valor);
    }
}
