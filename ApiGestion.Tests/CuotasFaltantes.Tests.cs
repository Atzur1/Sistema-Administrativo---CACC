namespace ApiGestion.Tests;

using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using ServiceLibrary;

// Aviso de cuotas faltantes: se revisan los últimos 12 meses cerrados (sin el último que ya corresponde, que lo
// asegura el generador mensual) y solo esos se pueden generar, siempre por decisión del SuperAdmin.
public class CuotasFaltantesServiceTests
{
    private sealed class FakeDao : ICuotasFaltantesDao
    {
        public (DateTime Desde, DateTime Hasta)? Ventana;
        public DateTime? Generado;

        public IReadOnlyList<MesConCuotasFaltantes> ObtenerMesesConFaltantes(DateTime desde, DateTime hasta)
        {
            Ventana = (desde, hasta);
            return new List<MesConCuotasFaltantes>();
        }

        public IReadOnlyList<CuotaFaltante> ObtenerFaltantesDelMes(DateTime periodo) => new List<CuotaFaltante>();

        public int GenerarFaltantesDelMes(DateTime periodo)
        {
            Generado = periodo;
            return 45;
        }
    }

    [Theory]
    [InlineData(2026, 10, 11, "2025-10-01", "2026-09-01")] // desde el 10: octubre lo asegura el generador
    [InlineData(2026, 10, 9, "2025-09-01", "2026-08-01")]  // antes del 10: septiembre lo asegura el generador
    public void RevisaLosUltimos12MesesCerrados(int anio, int mes, int dia, string desde, string hasta)
    {
        var dao = new FakeDao();
        new CuotasFaltantesService(dao, () => new DateTime(anio, mes, dia)).ObtenerMesesConFaltantes();

        Assert.Equal((DateTime.Parse(desde), DateTime.Parse(hasta)), dao.Ventana);
    }

    [Fact]
    public void GeneraUnMesRevisado()
    {
        var dao = new FakeDao();
        int creadas = new CuotasFaltantesService(dao, () => new DateTime(2026, 10, 11)).GenerarFaltantesDelMes(2026, 9);

        Assert.Equal(45, creadas);
        Assert.Equal(new DateTime(2026, 9, 1), dao.Generado);
    }

    [Theory]
    [InlineData(2026, 10)] // el mes en curso: lo genera el sistema el 10
    [InlineData(2026, 11)] // un mes futuro
    [InlineData(2025, 9)]  // más de un año atrás
    [InlineData(2026, 13)] // un mes que no existe
    public void NoGeneraMesesFueraDeLosRevisados(int anio, int mes)
    {
        var dao = new FakeDao();
        var service = new CuotasFaltantesService(dao, () => new DateTime(2026, 10, 11));

        Assert.Throws<CuotasFaltantesInvalidasException>(() => service.GenerarFaltantesDelMes(anio, mes));
        Assert.Null(dao.Generado);
    }
}

// El SQL del aviso contra una base real: detecta el mes sin cuota con las mismas reglas que el generador y,
// una vez generado, deja de avisarlo. Todo dentro de una transacción que se revierte.
[Collection(BaseDeDatosCollection.Nombre)]
public class CuotasFaltantesDaoIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    [IntegracionFact]
    public void DetectaElMesSinCuotaYDejaDeAvisarloAlGenerarlo()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            // Una categoría propia de la prueba, para que los jugadores reales no cuenten.
            int categoria;
            using (var cmd = new SqlCommand(
                "DECLARE @n TABLE (id INT); INSERT INTO CATEGORIAS (nombre_categoria) OUTPUT INSERTED.PK_id_categoria INTO @n VALUES ('Prueba faltantes'); SELECT id FROM @n;", connection, transaction))
            {
                categoria = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqlCommand(
                "INSERT INTO ARANCELES (genero, FK_id_categoria, monto, vigente_desde) VALUES (NULL, @c, 7777, '2097-01-01')", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@c", categoria);
                cmd.ExecuteNonQuery();
            }
            var player = new Player
            {
                FirstName = "Prueba", LastName = "Faltantes", Dni = "98" + Random.Shared.Next(100000, 999999),
                BirthDate = new DateTime(2010, 1, 1), Gender = "Femenino", CategoryId = categoria, JoinDate = new DateTime(2097, 3, 5)
            };
            int jugador = PlayerDAO.InsertPlayer(connection, transaction, PlayerDAO.InsertPerson(connection, transaction, player), player);

            // (Los jugadores reales de la base también pueden faltar en 2097: se mira solo al de la prueba.)
            bool Falta(int mes) => CuotasFaltantesDao.ObtenerFaltantesDelMesCore(connection, transaction, new DateTime(2097, mes, 1))
                .Any(c => c.IdJugador == jugador);

            Assert.False(Falta(2)); // todavía no estaba en el club
            Assert.True(Falta(3));  // mes del alta, sin cuota
            Assert.True(Falta(4));

            var cuota = CuotasFaltantesDao.ObtenerFaltantesDelMesCore(connection, transaction, new DateTime(2097, 3, 1)).Single(c => c.IdJugador == jugador);
            Assert.Equal(7777m, cuota.Monto);
            Assert.Equal("Prueba faltantes", cuota.Categoria);

            // El resumen por mes cuenta lo mismo que el detalle.
            var marzo = CuotasFaltantesDao.ObtenerMesesConFaltantesCore(connection, transaction, new DateTime(2097, 2, 1), new DateTime(2097, 4, 1))
                .Single(m => m.Periodo == new DateTime(2097, 3, 1));
            Assert.Equal(CuotasFaltantesDao.ObtenerFaltantesDelMesCore(connection, transaction, new DateTime(2097, 3, 1)).Count, marzo.Cantidad);

            Assert.True(SqlCuotaMensual.Generar(connection, transaction, null, categoria, null, 3, 2097) > 0);

            Assert.False(Falta(3)); // ya generada: deja de avisarse
            Assert.True(Falta(4));
        }
        finally { transaction.Rollback(); }
    }
}
