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

    public static int? CurrentUserId
    {
        get => Actor.Value;
        set => Actor.Value = value;
    }

    public static IDisposable Push(int? userId)
    {
        var previous = Actor.Value;
        Actor.Value = userId;
        return new RestoreScope(previous);
    }

    private sealed class RestoreScope(int? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            Actor.Value = previous;
            _disposed = true;
        }
    }
}

/// <summary>Opens a pooled connection and stamps its SQL session with the current request actor.</summary>
public static class SqlConnectionFactory
{
    public static SqlConnection Open(string connectionString)
    {
        var connection = new SqlConnection(connectionString);
        try
        {
            connection.Open();
            using var command = new SqlCommand(
                "EXEC sys.sp_set_session_context @key=N'idUsuario', @value=@idUsuario;",
                connection);
            command.Parameters.Add("@idUsuario", SqlDbType.Int).Value =
                SqlAuditActorContext.CurrentUserId is int id ? id : DBNull.Value;
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
