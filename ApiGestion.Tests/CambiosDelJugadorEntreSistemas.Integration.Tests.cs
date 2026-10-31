namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-065: los cambios que el Portal Deportivo hace sobre un jugador (categoría, baja, reincorporación) y los aranceles que
// programa el Administrativo impactan bien en las cuotas. El Deportivo escribe directo en la base compartida, así que acá
// esos cambios se hacen igual: con un UPDATE sobre JUGADORES. Las cuotas se emiten con el mismo SQL que el generador
// mensual y la programación de aranceles (SqlCuotaMensual).
//
// Cada caso corre en una transacción que se revierte, con categorías y aranceles propios en un año que no choca con datos
// reales (2097): así no depende de los aranceles que tenga cargados cada base.
[Collection(BaseDeDatosCollection.Nombre)]
public class CambiosDelJugadorEntreSistemasIntegrationTests
{
    private const int Anio = 2097;

    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    private static void EnTransaccion(Action<SqlConnection, SqlTransaction, PagosDao> caso)
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try { caso(connection, transaction, new PagosDao(Conexion)); }
        finally { transaction.Rollback(); }
    }

    private static int Escalar(SqlConnection c, SqlTransaction t, string sql, params (string Nombre, object Valor)[] parametros)
    {
        using var cmd = new SqlCommand(sql, c, t);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Una categoría propia de la prueba, sin aranceles de nadie.
    private static int Categoria(SqlConnection c, SqlTransaction t) => Escalar(c, t, @"
        DECLARE @nueva TABLE (id INT);
        INSERT INTO CATEGORIAS (nombre_categoria, asociacion) OUTPUT INSERTED.PK_id_categoria INTO @nueva (id)
        VALUES (CONCAT('HU065 ', NEWID()), 'Liga Cordobesa');
        SELECT id FROM @nueva;");

    private static void Arancel(SqlConnection c, SqlTransaction t, string? genero, int? categoria, decimal monto, int desdeMes)
    {
        using var cmd = new SqlCommand("INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (@g, @c, @m, @d)", c, t);
        cmd.Parameters.AddWithValue("@g", (object?)genero ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@c", (object?)categoria ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@m", monto);
        cmd.Parameters.AddWithValue("@d", new DateTime(Anio, desdeMes, 1));
        cmd.ExecuteNonQuery();
    }

    private static (int Id, string Dni) Jugador(SqlConnection c, SqlTransaction t, int categoria, string genero = Generos.Masculino, int altaMes = 1)
    {
        var player = new Player
        {
            FirstName = "Prueba", LastName = "Hu065", Dni = "92" + Random.Shared.Next(100000, 999999), BirthDate = new DateTime(2010, 1, 1),
            Gender = genero, CategoryId = categoria, JoinDate = new DateTime(Anio, altaMes, 1)
        };
        int id = PlayerDAO.InsertPlayer(c, t, PlayerDAO.InsertPerson(c, t, player), player);
        return (id, player.Dni);
    }

    // Lo que hace el Portal Deportivo: escribe directo en JUGADORES.
    private static void DeportivoActualiza(SqlConnection c, SqlTransaction t, int jugador, string set, params (string Nombre, object Valor)[] parametros)
    {
        using var cmd = new SqlCommand($"UPDATE JUGADORES SET {set} WHERE PK_id_jugador = @jugador", c, t);
        cmd.Parameters.AddWithValue("@jugador", jugador);
        foreach (var (nombre, valor) in parametros) cmd.Parameters.AddWithValue(nombre, valor);
        cmd.ExecuteNonQuery();
    }

    // Monto de la cuota del mes (null si no tiene).
    private static decimal? Cuota(SqlConnection c, SqlTransaction t, int jugador, int mes)
    {
        using var cmd = new SqlCommand(@"
            SELECT monto_base FROM PAGOS WHERE FK_id_jugador = @j AND concepto = 'Cuota'
              AND fecha_vencimiento >= @desde AND fecha_vencimiento < DATEADD(MONTH, 1, @desde)", c, t);
        cmd.Parameters.AddWithValue("@j", jugador);
        cmd.Parameters.AddWithValue("@desde", new DateTime(Anio, mes, 1));
        var valor = cmd.ExecuteScalar();
        return valor is null or DBNull ? null : Convert.ToDecimal(valor);
    }

    // Cambio de categoría (HU-010): desde la próxima cuota que se emite paga el arancel de la categoría nueva; las cuotas
    // ya emitidas (pagadas o no) conservan su monto.
    [IntegracionFact]
    public void CambioDeCategoria_LaProximaCuotaTomaElArancelNuevoYLasEmitidasNoCambian()
    {
        EnTransaccion((c, t, dao) =>
        {
            int sub15 = Categoria(c, t), sub17 = Categoria(c, t);
            Arancel(c, t, null, sub15, 10_000m, 1);
            Arancel(c, t, null, sub17, 15_000m, 1);
            var jugador = Jugador(c, t, sub15);

            Assert.True(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 1, Anio));
            Assert.True(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 2, Anio));
            var febrero = dao.ObtenerPagoPendienteDeJugadorEnPeriodo(c, t, jugador.Id, 2, Anio)!;
            dao.InsertarPago(c, t, new Pago
            {
                IdJugador = jugador.Id, MontoBase = 10_000m, MontoFinal = 10_000m, MetodoPago = "Efectivo",
                FechaPago = new DateTime(Anio, 2, 12), FechaVencimiento = new DateTime(Anio, 2, 1), Estado = true
            });
            dao.EliminarPago(c, t, febrero.IdPago);

            DeportivoActualiza(c, t, jugador.Id, "FK_id_categoria = @categoria", ("@categoria", sub17));

            Assert.True(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 3, Anio));
            Assert.Equal(15_000m, Cuota(c, t, jugador.Id, 3));
            Assert.Equal(10_000m, Cuota(c, t, jugador.Id, 1)); // emitida y pendiente: no cambia
            Assert.Equal(10_000m, Cuota(c, t, jugador.Id, 2)); // emitida y pagada: no cambia
            Assert.False(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 1, Anio)); // no se vuelve a emitir con el monto nuevo
        });
    }

    // Arancel nuevo de categoría (HU-049): al programarlo, solo reciben el monto nuevo los jugadores de esa categoría y
    // desde su mes de vigencia; los de otra categoría siguen con el suyo y el mes anterior no cambia.
    [IntegracionFact]
    public void ArancelNuevoDeCategoria_SoloAfectaASuCategoriaDesdeSuMes()
    {
        EnTransaccion((c, t, dao) =>
        {
            int sub15 = Categoria(c, t), sub17 = Categoria(c, t);
            Arancel(c, t, null, sub15, 10_000m, 1);
            Arancel(c, t, null, sub17, 15_000m, 1);
            var delSub15 = Jugador(c, t, sub15);
            var delSub17 = Jugador(c, t, sub17);
            dao.GenerarCuotasPendientesDelMes(c, t, null, sub15, 4, Anio);
            dao.GenerarCuotasPendientesDelMes(c, t, null, sub17, 4, Anio);

            // Se programa para la Sub 15 desde mayo, como lo hace ArancelesService: el arancel y la emisión de su alcance.
            Arancel(c, t, null, sub15, 12_000m, 5);
            dao.GenerarCuotasPendientesDelMes(c, t, null, sub15, 5, Anio);

            Assert.Equal(12_000m, Cuota(c, t, delSub15.Id, 5));
            Assert.Null(Cuota(c, t, delSub17.Id, 5));             // la programación no le emitió nada
            Assert.Equal(10_000m, Cuota(c, t, delSub15.Id, 4));   // el mes anterior no cambia

            dao.GenerarCuotasPendientesDelMes(c, t, null, sub17, 5, Anio); // su cuota de mayo, con el generador
            Assert.Equal(15_000m, Cuota(c, t, delSub17.Id, 5));
        });
    }

    // Arancel nuevo de género (HU-049): alcanza solo a ese género, y no a quien tiene arancel de su categoría.
    [IntegracionFact]
    public void ArancelNuevoDeGenero_SoloAfectaAEseGeneroSinArancelDeCategoria()
    {
        EnTransaccion((c, t, dao) =>
        {
            int sinArancel = Categoria(c, t), conArancel = Categoria(c, t);
            Arancel(c, t, null, conArancel, 15_000m, 1);
            var jugadora = Jugador(c, t, sinArancel, Generos.Femenino);
            var jugador = Jugador(c, t, sinArancel, Generos.Masculino);
            var jugadoraConCategoria = Jugador(c, t, conArancel, Generos.Femenino);

            Arancel(c, t, Generos.Femenino, null, 9_000m, 6);
            dao.GenerarCuotasPendientesDelMes(c, t, Generos.Femenino, null, 6, Anio);

            Assert.Equal(9_000m, Cuota(c, t, jugadora.Id, 6));
            Assert.Null(Cuota(c, t, jugador.Id, 6));                          // otro género: no lo alcanza
            Assert.Equal(15_000m, Cuota(c, t, jugadoraConCategoria.Id, 6));   // su categoría manda sobre el género
            Assert.False(dao.EmitirCuotaDeJugador(c, t, jugadora.Id, 5, Anio)); // antes de la vigencia no hay arancel
        });
    }

    // Baja (HU-057): dado de baja por el Deportivo, paga el mes de la baja pero no recibe cuotas de los meses siguientes, y
    // lo que debía sigue pendiente.
    [IntegracionFact]
    public void Baja_NoRecibeCuotasDeLosMesesSiguientesYLaDeudaSigue()
    {
        EnTransaccion((c, t, dao) =>
        {
            int categoria = Categoria(c, t);
            Arancel(c, t, null, categoria, 10_000m, 1);
            var jugador = Jugador(c, t, categoria);
            foreach (int mes in new[] { 1, 2 }) Assert.True(dao.EmitirCuotaDeJugador(c, t, jugador.Id, mes, Anio));

            DeportivoActualiza(c, t, jugador.Id, "fecha_baja = @baja", ("@baja", new DateTime(Anio, 3, 15)));

            Assert.True(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 3, Anio));   // el mes de la baja estuvo en el club
            dao.GenerarCuotasPendientesDelMes(c, t, null, categoria, 4, Anio);   // el generador de abril
            Assert.Null(Cuota(c, t, jugador.Id, 4));
            Assert.False(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 5, Anio));
            foreach (int mes in new[] { 1, 2, 3 })
                Assert.NotNull(dao.ObtenerPagoPendienteDeJugadorEnPeriodo(c, t, jugador.Id, mes, Anio));
            Assert.DoesNotContain(CuotasFaltantesDao.ObtenerFaltantesDelMesCore(c, t, new DateTime(Anio, 4, 1)), f => f.IdJugador == jugador.Id);
        });
    }

    // Reincorporación: el Deportivo le quita la baja y le pone como alta el día en que vuelve. Recibe cuota desde ese mes, y
    // los meses en que estuvo afuera no se le cobran ni figuran como cuotas faltantes.
    [IntegracionFact]
    public void Reincorporacion_VuelveARecibirCuotaDesdeEseMes()
    {
        EnTransaccion((c, t, dao) =>
        {
            int categoria = Categoria(c, t);
            Arancel(c, t, null, categoria, 10_000m, 1);
            var jugador = Jugador(c, t, categoria);
            DeportivoActualiza(c, t, jugador.Id, "fecha_baja = @baja", ("@baja", new DateTime(Anio, 3, 15)));

            DeportivoActualiza(c, t, jugador.Id, "fecha_baja = NULL, fecha_alta = @alta", ("@alta", new DateTime(Anio, 7, 5)));

            Assert.True(dao.EmitirCuotaDeJugador(c, t, jugador.Id, 7, Anio));
            Assert.Equal(10_000m, Cuota(c, t, jugador.Id, 7));
            foreach (int mes in new[] { 4, 5, 6 })
            {
                Assert.False(dao.EmitirCuotaDeJugador(c, t, jugador.Id, mes, Anio));
                Assert.DoesNotContain(CuotasFaltantesDao.ObtenerFaltantesDelMesCore(c, t, new DateTime(Anio, mes, 1)), f => f.IdJugador == jugador.Id);
            }
        });
    }

    // Estado tras la baja, en lo que lee el Deportivo: aparece como "baja" y sin acceso aunque deba, con su deuda intacta.
    [IntegracionFact]
    public void EstadoTrasLaBaja_ElDeportivoLoVeDeBajaYSinAcceso()
    {
        EnTransaccion((c, t, dao) =>
        {
            int categoria = Categoria(c, t);
            var jugador = Jugador(c, t, categoria);
            DeportivoActualiza(c, t, jugador.Id, "fecha_baja = @baja", ("@baja", new DateTime(Anio, 3, 15)));

            var estado = Assert.Single(EstadoDeudaDao.ObtenerPorDniCore(c, t, new[] { jugador.Dni }, null));
            Assert.Equal(EstadoDeportivo.Baja, estado.Estado);
            Assert.False(estado.AccesoPermitido);
            Assert.Equal(new DateTime(Anio, 3, 15), estado.FechaBaja);
        });
    }
}

// Pruebas que GRABAN datos (las pantallas leen con su propia conexión y no ven una transacción sin confirmar): solo corren
// contra una copia descartable de la base, con "Descartable" en el nombre.
public sealed class IntegracionDescartableFactAttribute : FactAttribute
{
    public const string Variable = "CACC_TEST_DB_DESCARTABLE";

    public IntegracionDescartableFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Prueba que graba datos: definí {Variable} con la conexión a una COPIA descartable de la base (con \"Descartable\" en el nombre).";
        }
    }
}

[Collection(BaseDeDatosCollection.Nombre)]
public class BajaConDeudaEnPantallasIntegrationTests
{
    private static readonly Lazy<string> ConexionVerificada = new(() =>
    {
        string conexion = Environment.GetEnvironmentVariable(IntegracionDescartableFactAttribute.Variable)!;
        using var connection = new SqlConnection(conexion);
        connection.Open();
        using var cmd = new SqlCommand("SELECT DB_NAME()", connection);
        string baseActual = (string)cmd.ExecuteScalar();
        if (!baseActual.Contains("Descartable", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La base '{baseActual}' no parece descartable: esta prueba graba datos.");
        return conexion;
    });

    // Estado tras la baja (HU-057): un jugador con cuotas vencidas al que el Deportivo da de baja sigue en Deudas y Morosidad
    // con toda su deuda (para cobranzas), y el Deportivo lo lee como "baja", sin acceso.
    [IntegracionDescartableFact]
    public void UnJugadorDadoDeBajaConDeuda_SigueEnDeudasYMorosidadYElDeportivoLoVeDeBaja()
    {
        string conexion = ConexionVerificada.Value;
        int jugador;
        string dni;
        using (var connection = new SqlConnection(conexion))
        {
            connection.Open();
            using var transaction = connection.BeginTransaction();
            int categoria;
            using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction))
                categoria = Convert.ToInt32(cmd.ExecuteScalar());
            var player = new Player
            {
                FirstName = "Prueba", LastName = "Hu065Baja", Dni = "91" + Random.Shared.Next(100000, 999999), BirthDate = new DateTime(2010, 1, 1),
                Gender = Generos.Masculino, CategoryId = categoria, JoinDate = new DateTime(2024, 1, 1)
            };
            jugador = PlayerDAO.InsertPlayer(connection, transaction, PlayerDAO.InsertPerson(connection, transaction, player), player);
            dni = player.Dni;
            foreach (int mes in new[] { 1, 2, 3 })
            {
                using var cuota = new SqlCommand(
                    "INSERT INTO PAGOS (FK_id_jugador, monto_base, monto_final, fecha_vencimiento, estado, concepto) VALUES (@j, 10000, 10000, @p, 0, 'Cuota')",
                    connection, transaction);
                cuota.Parameters.AddWithValue("@j", jugador);
                cuota.Parameters.AddWithValue("@p", new DateTime(2024, mes, 1));
                cuota.ExecuteNonQuery();
            }
            // El Deportivo lo da de baja.
            using (var baja = new SqlCommand("UPDATE JUGADORES SET fecha_baja = '2024-04-15' WHERE PK_id_jugador = @j", connection, transaction))
            {
                baja.Parameters.AddWithValue("@j", jugador);
                baja.ExecuteNonQuery();
            }
            transaction.Commit();
        }

        // Deudas y Morosidad (GET api/pagos/player-accounts?onlyDebtors=true).
        var deudor = Assert.Single(new PagosDao(conexion).GetPlayerAccounts(onlyDebtors: true), a => a.PlayerId == jugador);
        Assert.Equal(30_000m, deudor.AmountOwed);
        Assert.Equal(3, deudor.OverdueInstallments);
        Assert.Equal(new DateTime(2024, 4, 15), deudor.LeaveDate);

        // Lo que lee el Deportivo (GET api/integracion/v1/estado-deuda/dni/{dni}).
        var estado = Assert.Single(new EstadoDeudaDao(conexion).ObtenerPorDni(new[] { dni }, null));
        Assert.Equal(EstadoDeportivo.Baja, estado.Estado);
        Assert.False(estado.AccesoPermitido);
        Assert.Equal(30_000m, estado.DeudaTotal);
    }
}
