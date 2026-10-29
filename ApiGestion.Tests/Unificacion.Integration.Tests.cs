namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-053: con la base unificada, el Portal Administrativo y el Portal Deportivo escriben a la vez sobre las mismas
// tablas. Estas pruebas corren el SQL real de PlayerDAO sobre una base real: varias altas simultáneas, cada una con su
// propia conexión y su transacción abierta al mismo tiempo, tienen que recibir ids distintos de PERSONAS (IDENTITY) y
// de JUGADORES y PAGOS (SEQ_JUGADORES / SEQ_PAGOS). Con MAX(id) + 1 dos altas leen el mismo máximo y una de las dos
// choca. Cada alta se revierte: no dejan datos.
[Collection(BaseDeDatosCollection.Nombre)]
public class UnificacionIdsIntegrationTests
{
    private const int Altas = 8;

    [IntegracionFact]
    public async Task ConcurrentRegistrations_GetDistinctIdsFromIdentityAndSequences()
    {
        string connectionString = Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;
        using var largada = new Barrier(Altas);

        var altas = await Task.WhenAll(Enumerable.Range(0, Altas)
            .Select(n => Task.Run(() => Register(connectionString, n, largada))));

        Assert.Equal(Altas, altas.Select(x => x.PersonId).Distinct().Count());
        Assert.Equal(Altas, altas.Select(x => x.PlayerId).Distinct().Count());
        Assert.Equal(Altas, altas.Select(x => x.ChargeId).Distinct().Count());
        Assert.All(altas, x => Assert.True(x.PersonId > 0 && x.PlayerId > 0 && x.ChargeId > 0, "Cada alta tiene que devolver sus ids."));
    }

    [IntegracionFact]
    public void Registration_TakesIdsAboveTheHighestExistingOnes()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            int maxPlayer = Scalar(connection, transaction, "SELECT ISNULL(MAX(PK_id_jugador), 0) FROM JUGADORES");
            int maxCharge = Scalar(connection, transaction, "SELECT ISNULL(MAX(PK_id_pago), 0) FROM PAGOS");

            var registered = Register(connection, transaction, 0);

            Assert.True(registered.PlayerId > maxPlayer, "SEQ_JUGADORES tiene que dar un id mayor que todos los presentes.");
            Assert.True(registered.ChargeId > maxCharge, "SEQ_PAGOS tiene que dar un id mayor que todos los presentes.");
        }
        finally
        {
            transaction.Rollback();
        }
    }

    private static (int PersonId, int PlayerId, int ChargeId) Register(string connectionString, int n, Barrier largada)
    {
        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            // Todas las transacciones abiertas antes de que cualquiera inserte: las altas se superponen de verdad.
            Assert.True(largada.SignalAndWait(TimeSpan.FromSeconds(30)), "Las altas no llegaron a largar juntas.");
            return Register(connection, transaction, n);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    private static (int PersonId, int PlayerId, int ChargeId) Register(SqlConnection connection, SqlTransaction transaction, int n)
    {
        var player = new Player
        {
            FirstName = "PRUEBA",
            LastName = "CONCURRENCIA",
            Dni = (98000000 + n).ToString(),
            BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino",
            CategoryId = Scalar(connection, transaction, "SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria"),
            JoinDate = new DateTime(2026, 10, 1)
        };

        int personId = PlayerDAO.InsertPerson(connection, transaction, player);
        int playerId = PlayerDAO.InsertPlayer(connection, transaction, personId, player);
        PlayerDAO.InsertPendingCharge(connection, transaction, playerId, 85000m, new DateTime(2026, 10, 1), "Cuota");

        using var command = new SqlCommand("SELECT PK_id_pago FROM PAGOS WHERE FK_id_jugador = @id", connection, transaction);
        command.Parameters.AddWithValue("@id", playerId);
        int chargeId = Convert.ToInt32(command.ExecuteScalar());

        return (personId, playerId, chargeId);
    }

    private static int Scalar(SqlConnection connection, SqlTransaction transaction, string query)
    {
        using var command = new SqlCommand(query, connection, transaction);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}
