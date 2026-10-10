namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// SQL de la generación de cuotas por alta/baja, de la anulación de pagos y de la hora del club (SqlReloj),
// ejecutado contra una base real con los triggers de auditoría. Todo dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class PagosDaoAltaBajaYAnulacionIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    // Un jugador de prueba con un arancel propio de su categoría en un año que no choca con datos reales.
    private static int CrearJugador(SqlConnection connection, SqlTransaction transaction, DateTime alta, DateTime? baja, int idCategoria)
    {
        var player = new Player
        {
            FirstName = "Prueba", LastName = "Integracion", Dni = "99" + Random.Shared.Next(100000, 999999),
            BirthDate = new DateTime(2010, 1, 1), Gender = "Masculino", CategoryId = idCategoria, JoinDate = alta
        };
        int personId = PlayerDAO.InsertPerson(connection, transaction, player);
        int playerId = PlayerDAO.InsertPlayer(connection, transaction, personId, player);
        if (baja.HasValue)
        {
            using var cmd = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @b WHERE PK_id_jugador = @id", connection, transaction);
            cmd.Parameters.AddWithValue("@b", baja.Value);
            cmd.Parameters.AddWithValue("@id", playerId);
            cmd.ExecuteNonQuery();
        }
        return playerId;
    }

    private static int PrimeraCategoria(SqlConnection connection, SqlTransaction transaction)
    {
        using var cmd = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void ArancelDeCategoria(SqlConnection connection, SqlTransaction transaction, int idCategoria, decimal monto, DateTime desde)
    {
        using var cmd = new SqlCommand("INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (NULL, @c, @m, @d)", connection, transaction);
        cmd.Parameters.AddWithValue("@c", idCategoria);
        cmd.Parameters.AddWithValue("@m", monto);
        cmd.Parameters.AddWithValue("@d", desde);
        cmd.ExecuteNonQuery();
    }

    [IntegracionFact]
    public void LaCuotaSoloSeEmiteEnLosMesesEnQueElJugadorEstuvoEnElClub()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            ArancelDeCategoria(connection, transaction, categoria, 12_345m, new DateTime(2098, 1, 1));
            int jugador = CrearJugador(connection, transaction, alta: new DateTime(2098, 3, 10), baja: new DateTime(2098, 5, 20), categoria);

            Assert.False(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 2, 2098)); // antes del alta
            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 3, 2098));  // mes del alta
            Assert.True(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 5, 2098));  // mes de la baja
            Assert.False(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 6, 2098)); // después de la baja
            Assert.False(dao.EmitirCuotaDeJugador(connection, transaction, jugador, 3, 2098)); // ya la tiene

            var cuota = dao.ObtenerPagoPendienteDeJugadorEnPeriodo(connection, transaction, jugador, 3, 2098);
            Assert.NotNull(cuota);
            Assert.Equal(12_345m, cuota!.MontoFinal);
            Assert.Null(dao.ObtenerDescuentoAplicableEnPeriodo(connection, transaction, jugador, cuota.FechaVencimiento!.Value));
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void AnularUnAbono_LoRegistraEnPagosAnuladosYLoSacaDePagos()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            var dao = new PagosDao(Conexion);
            int categoria = PrimeraCategoria(connection, transaction);
            int jugador = CrearJugador(connection, transaction, alta: new DateTime(2098, 1, 1), baja: null, categoria);
            int idUsuario;
            using (var cmd = new SqlCommand("SELECT TOP (1) PK_id_usuario FROM USUARIO ORDER BY PK_id_usuario", connection, transaction))
                idUsuario = Convert.ToInt32(cmd.ExecuteScalar());

            int idAbono = dao.InsertarPago(connection, transaction, new Pago
            {
                IdJugador = jugador, MontoBase = 10_000m, MontoFinal = 10_000m, MetodoPago = "Efectivo",
                FechaPago = new DateTime(2098, 1, 5), FechaVencimiento = new DateTime(2098, 1, 1), Estado = true, IdUsuarioRegistro = idUsuario
            });

            var abono = dao.ObtenerPagoParaAnular(connection, transaction, idAbono);
            Assert.NotNull(abono);
            Assert.Equal("Cuota", abono!.Concepto);
            Assert.Single(dao.ObtenerFilasDeLaMismaDeuda(connection, transaction, abono));

            dao.RegistrarAnulacionYEliminarAbono(connection, transaction, abono, "Prueba de integración", idUsuario);

            Assert.Null(dao.ObtenerPagoParaAnular(connection, transaction, idAbono));
            using var anulado = new SqlCommand("SELECT monto, motivo, fecha_hora_anulacion FROM PAGOS_ANULADOS WHERE id_pago = @id", connection, transaction);
            anulado.Parameters.AddWithValue("@id", idAbono);
            using var reader = anulado.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(10_000m, reader.GetDecimal(0));
            Assert.Equal("Prueba de integración", reader.GetString(1));
        }
        finally { transaction.Rollback(); }
    }

    [IntegracionFact]
    public void LaHoraDelClubSaleEnHoraArgentina()
    {
        using var connection = SqlConnectionFactory.Open(Conexion);
        using var cmd = new SqlCommand($"SELECT {SqlReloj.Ahora}, {SqlReloj.Hoy}, SYSUTCDATETIME()", connection);
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());

        var diferencia = reader.GetDateTime(2) - reader.GetDateTime(0);
        Assert.InRange(diferencia.TotalHours, 2.99, 3.01); // UTC-3, sin horario de verano
        Assert.Equal(reader.GetDateTime(0).Date, reader.GetDateTime(1));
        Assert.InRange((RelojNegocio.Ahora - reader.GetDateTime(0)).TotalMinutes, -2, 2);
    }

    // Consultas de solo lectura cuyo SQL cambió (GETDATE -> SqlReloj): se ejecutan para comprobar que siguen siendo válidas.
    [IntegracionFact]
    public void LasConsultasConLaHoraDelClubSiguenSiendoValidas()
    {
        var pagos = new PagosDao(Conexion);
        pagos.ObtenerResumen();
        pagos.ObtenerResumenHoy();
        new EstadisticasDao(Conexion).ObtenerResumenGeneral();
        new DiscountDao(Conexion).GetActiveDiscounts();
        new DiscountDao(Conexion).GetAllDiscounts();
        new EnrollmentFeeDAO(Conexion).GetCurrentEnrollmentFee();
        Assert.NotNull(new JugadoresDao(Conexion).ListarJugadores());
    }
}
