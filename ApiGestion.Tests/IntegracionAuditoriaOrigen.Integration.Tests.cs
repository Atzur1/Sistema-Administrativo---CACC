namespace ApiGestion.Tests;

using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-055: una escritura que entra por la API de integración queda en AUDITORIA_CAMBIOS con las dos dimensiones: el sistema
// de origen (origen_sistema = PortalDeportivo, de la credencial de sistema) y el operador (id_usuario, del token). Si la
// operación se revierte, su evento también: nunca figura como una modificación hecha.
[Collection(BaseDeDatosCollection.Nombre)]
public class IntegracionAuditoriaOrigenIntegrationTests
{
    private static string Conexion => Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable)!;

    [IntegracionFact]
    public void AWriteFromTheIntegration_IsAuditedWithTheDeportivoAsOriginAndTheOperator()
    {
        int? operador = PrimerUsuario();
        using (SqlAuditActorContext.Push(operador))
        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            using SqlConnection connection = SqlConnectionFactory.Open(Conexion);
            using SqlTransaction transaction = connection.BeginTransaction();
            try
            {
                int personId = PlayerDAO.InsertPerson(connection, transaction, Persona("97000001"));

                using var command = new SqlCommand(
                    "SELECT TOP (1) origen_sistema, id_usuario FROM AUDITORIA_CAMBIOS WHERE entidad = N'PERSONAS' AND id_entidad = @id ORDER BY PK_id_evento DESC",
                    connection, transaction);
                command.Parameters.AddWithValue("@id", personId.ToString());
                using var reader = command.ExecuteReader();
                Assert.True(reader.Read(), "El alta tiene que quedar auditada.");
                Assert.Equal(IntegrationClientCredentials.SistemaDeportivo, reader.GetString(0));
                Assert.Equal(operador, reader.IsDBNull(1) ? null : reader.GetInt32(1));
            }
            finally
            {
                transaction.Rollback();
            }
        }
    }

    [IntegracionFact]
    public void AWriteThatIsRolledBack_LeavesNoAuditEvent()
    {
        int personId;
        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            using SqlConnection connection = SqlConnectionFactory.Open(Conexion);
            using SqlTransaction transaction = connection.BeginTransaction();
            personId = PlayerDAO.InsertPerson(connection, transaction, Persona("97000002"));
            transaction.Rollback();
        }

        using var check = SqlConnectionFactory.Open(Conexion);
        using var command = new SqlCommand("SELECT COUNT(*) FROM AUDITORIA_CAMBIOS WHERE entidad = N'PERSONAS' AND id_entidad = @id", check);
        command.Parameters.AddWithValue("@id", personId.ToString());
        Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar()));
    }

    [IntegracionFact]
    public void APooledConnection_DoesNotKeepTheOriginOfThePreviousRequest()
    {
        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            using SqlConnection integracion = SqlConnectionFactory.Open(Conexion);
            Assert.Equal(IntegrationClientCredentials.SistemaDeportivo, Origen(integracion));
        }

        // La siguiente conexión sale del mismo pool: un pedido del Portal Administrativo no hereda el origen.
        using SqlConnection portal = SqlConnectionFactory.Open(Conexion);
        Assert.Equal(SqlAuditActorContext.PortalAdministrativo, Origen(portal));
    }

    // La base es compartida con el Portal Deportivo (HU-053): su backend y los scripts de mantenimiento escriben sin pasar
    // por esta API. Por eso la API siempre se identifica, y NULL queda para "no pasó por la API" en lugar de confundirse
    // con el Portal Administrativo.
    [IntegracionFact]
    public void AWriteFromThePortal_IsAuditedWithThePortalAsOrigin_AndOneOutsideTheApiHasNone()
    {
        using (SqlAuditActorContext.Push(PrimerUsuario()))
        {
            using SqlConnection connection = SqlConnectionFactory.Open(Conexion);
            using SqlTransaction transaction = connection.BeginTransaction();
            try
            {
                int personId = PlayerDAO.InsertPerson(connection, transaction, Persona("97000003"));
                Assert.Equal(SqlAuditActorContext.PortalAdministrativo, OrigenDelEvento(connection, transaction, personId));
            }
            finally
            {
                transaction.Rollback();
            }
        }

        // Sin pool: una conexión abierta como lo haría otro sistema, sin el sello de esta API.
        using var directa = new SqlConnection(new SqlConnectionStringBuilder(Conexion) { Pooling = false }.ConnectionString);
        directa.Open();
        using SqlTransaction fuera = directa.BeginTransaction();
        try
        {
            int personId = PlayerDAO.InsertPerson(directa, fuera, Persona("97000004"));
            Assert.Null(OrigenDelEvento(directa, fuera, personId));
        }
        finally
        {
            fuera.Rollback();
        }
    }

    private static string? OrigenDelEvento(SqlConnection connection, SqlTransaction transaction, int personId)
    {
        using var command = new SqlCommand(
            "SELECT TOP (1) origen_sistema FROM AUDITORIA_CAMBIOS WHERE entidad = N'PERSONAS' AND id_entidad = @id ORDER BY PK_id_evento DESC",
            connection, transaction);
        command.Parameters.AddWithValue("@id", personId.ToString());
        object? valor = command.ExecuteScalar();
        Assert.NotNull(valor);
        return valor is DBNull ? null : (string)valor;
    }

    private static string? Origen(SqlConnection connection)
    {
        using var command = new SqlCommand("SELECT CONVERT(nvarchar(50), SESSION_CONTEXT(N'origen'))", connection);
        object? valor = command.ExecuteScalar();
        return valor is DBNull or null ? null : (string)valor;
    }

    private static int? PrimerUsuario()
    {
        using var connection = new SqlConnection(Conexion);
        connection.Open();
        using var command = new SqlCommand("SELECT TOP (1) PK_id_usuario FROM USUARIOS ORDER BY PK_id_usuario", connection);
        object? valor = command.ExecuteScalar();
        return valor is null or DBNull ? null : Convert.ToInt32(valor);
    }

    private static Player Persona(string dni) => new()
    {
        FirstName = "PRUEBA",
        LastName = "AUDITORIA INTEGRACION",
        Dni = dni,
        BirthDate = new DateTime(2010, 1, 1),
        Gender = "Masculino"
    };
}
