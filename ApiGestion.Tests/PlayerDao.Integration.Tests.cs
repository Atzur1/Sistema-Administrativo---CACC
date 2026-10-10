namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// Marca una prueba que necesita una base de datos real. Sin la variable CACC_TEST_DB se omite (el resto
// de la suite sigue corriendo con fakes), y con ella apunta a una base de DESARROLLO, nunca a una compartida.
// Cada prueba trabaja dentro de una transacción que se revierte: no deja datos.
//
//   $env:CACC_TEST_DB = "Server=localhost;Database=ClubCamionerosPruebaCuatro;Trusted_Connection=True;TrustServerCertificate=True;"
//   dotnet test ApiGestion.Tests --filter "FullyQualifiedName~Integration"
public sealed class IntegracionFactAttribute : FactAttribute
{
    public const string Variable = "CACC_TEST_DB";

    public IntegracionFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Prueba de integración: definí la variable de entorno {Variable} con la conexión a una base de desarrollo.";
        }
    }
}

// HU-033: las pruebas del controlador usan un PlayerDAO falso, así que no ejecutan este SQL. Esta sí lo hace,
// sobre una base real con los triggers de auditoría puestos. Cubre el error 334 de "OUTPUT sin INTO" en PERSONA,
// que dejaba el alta de jugadores fallando en toda base con la auditoría aplicada.
[Collection(BaseDeDatosCollection.Nombre)]
public class PlayerDaoIntegrationTests
{
    [IntegracionFact]
    public void Registration_InsertsPersonPlayerAndChargeOnARealDatabaseWithAuditTriggers()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            var player = new Player
            {
                FirstName = "PRUEBA",
                LastName = "INTEGRACION",
                Dni = "99999999",
                BirthDate = new DateTime(2010, 1, 1),
                Gender = "Masculino",
                CategoryId = FirstCategoryId(connection, transaction),
                JoinDate = new DateTime(2026, 10, 1)
            };

            int personId = PlayerDAO.InsertPerson(connection, transaction, player);
            Assert.True(personId > 0, "La persona nueva tiene que devolver su id.");

            int playerId = PlayerDAO.InsertPlayer(connection, transaction, personId, player);
            Assert.True(playerId > 0, "El jugador nuevo tiene que devolver su id.");

            PlayerDAO.InsertPendingCharge(connection, transaction, playerId, 85000m, new DateTime(2026, 10, 1), "Cuota");

            Assert.Equal(1, Count(connection, transaction, "SELECT COUNT(*) FROM PERSONA WHERE PK_id_persona = @id", personId));
            Assert.Equal(1, Count(connection, transaction, "SELECT COUNT(*) FROM JUGADORES WHERE PK_id_jugador = @id", playerId));
            Assert.Equal(1, Count(connection, transaction,
                "SELECT COUNT(*) FROM PAGOS WHERE FK_id_jugador = @id AND concepto = 'Cuota' AND estado = 0", playerId));
        }
        finally
        {
            transaction.Rollback();
        }
    }

    private static long FirstCategoryId(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = new SqlCommand("SELECT TOP (1) PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction);
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static int Count(SqlConnection connection, SqlTransaction transaction, string query, int id)
    {
        using var command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(command.ExecuteScalar());
    }
}

// Las pruebas de integración comparten una misma base real: corren de a una, no en paralelo, para que
// las transacciones de una prueba no se crucen con las de otra.
[CollectionDefinition(Nombre, DisableParallelization = true)]
public sealed class BaseDeDatosCollection
{
    public const string Nombre = "Base de datos";
}
