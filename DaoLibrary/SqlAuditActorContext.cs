using System.Data;
using System.Threading;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

/// <summary>
/// Carries the authenticated operator to SQL Server triggers for the lifetime of an HTTP request.
/// Connections always set this value explicitly, including NULL, to avoid pooled-session leakage.
/// </summary>
public static class SqlAuditActorContext
{
    private static readonly AsyncLocal<int?> Actor = new();
    // HU-055: sistema que hizo el pedido (por ejemplo "PortalDeportivo" en la API de integración). Sin uno explícito, la
    // conexión se sella como PortalAdministrativo: la base es compartida con el Portal Deportivo (HU-053), así que en la
    // auditoría NULL queda para lo que no pasó por esta API (su backend, scripts de mantenimiento, eventos anteriores).
    // Es otra dimensión que el operador: una escritura de la integración tiene los dos.
    public const string PortalAdministrativo = "PortalAdministrativo";
    private static readonly AsyncLocal<string?> SourceSystem = new();

    public static int? CurrentUserId
    {
        get => Actor.Value;
        set => Actor.Value = value;
    }

    public static string? CurrentSystem => SourceSystem.Value;

    public static IDisposable Push(int? userId)
    {
        var previous = Actor.Value;
        Actor.Value = userId;
        return new RestoreScope(() => Actor.Value = previous);
    }

    public static IDisposable PushSystem(string? system)
    {
        var previous = SourceSystem.Value;
        SourceSystem.Value = system;
        return new RestoreScope(() => SourceSystem.Value = previous);
    }

    private sealed class RestoreScope(Action restore) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            restore();
            _disposed = true;
        }
    }
}

/// <summary>Opens a pooled connection and stamps its SQL session with the current request actor and source system.</summary>
public static class SqlConnectionFactory
{
    public static SqlConnection Open(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            using var command = new SqlCommand(
                "EXEC sys.sp_set_session_context @key=N'idUsuario', @value=@idUsuario; " +
                "EXEC sys.sp_set_session_context @key=N'origen', @value=@origen;",
                connection);
            command.Parameters.Add("@idUsuario", SqlDbType.Int).Value =
                SqlAuditActorContext.CurrentUserId is int id ? id : DBNull.Value;
            command.Parameters.Add("@origen", SqlDbType.NVarChar, 50).Value =
                SqlAuditActorContext.CurrentSystem ?? SqlAuditActorContext.PortalAdministrativo;
            command.ExecuteNonQuery();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }
}
