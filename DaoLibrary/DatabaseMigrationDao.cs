namespace DaoLibrary;

using System.Data;
using Microsoft.Data.SqlClient;

public class DatabaseMigrationDao
{
    private readonly string _connectionString;

    public DatabaseMigrationDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Takes the migrations lock, makes sure dbo.__CaccMigraciones exists, reads the applied history and asks
    // selectPending which scripts to run (it validates the history and throws if it does not match the code).
    // Each script runs in its own transaction together with its history row. Returns how many were applied.
    public async Task<int> ApplyPendingMigrationsAsync(
        Func<IReadOnlyDictionary<string, string>, IReadOnlyList<MigrationScript>> selectPending,
        Action<string> onApplied,
        Action<Exception> onLockReleaseFailed,
        CancellationToken cancellationToken)
    {
        // A dedicated connection without pooling closes the session even if acquiring the lock is cancelled;
        // it never leaves an applock held on a reusable connection.
        var connectionOptions = new SqlConnectionStringBuilder(_connectionString) { Pooling = false };
        await using var connection = new SqlConnection(connectionOptions.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        // The session lock covers the history and every script. It is released when the session closes.
        await using (var lockCommand = connection.CreateCommand())
        {
            lockCommand.CommandTimeout = 70;
            lockCommand.CommandText = """
                DECLARE @resultado int;
                EXEC @resultado = sys.sp_getapplock
                    @Resource = N'CACC:Migraciones', @LockMode = 'Exclusive',
                    @LockOwner = 'Session', @LockTimeout = 60000;
                SELECT @resultado;
                """;
            int lockResult = Convert.ToInt32(await lockCommand.ExecuteScalarAsync(cancellationToken));
            if (lockResult < 0)
            {
                throw new InvalidOperationException($"No se pudo obtener el bloqueo de migraciones (código {lockResult}). Otra instancia puede estar migrando; volver a intentar.");
            }
        }

        try
        {
            await CreateHistoryTableAsync(connection, cancellationToken);
            Dictionary<string, string> history = await ReadHistoryAsync(connection, cancellationToken);

            IReadOnlyList<MigrationScript> pending = selectPending(history);
            foreach (MigrationScript script in pending)
            {
                await ApplyScriptAsync(connection, script, cancellationToken);
                onApplied(script.Name);
            }
            return pending.Count;
        }
        finally
        {
            await ReleaseLockAsync(connection, onLockReleaseFailed);
        }
    }

    private static async Task CreateHistoryTableAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SET XACT_ABORT ON;
            IF OBJECT_ID(N'dbo.__CaccMigraciones', N'U') IS NULL
            CREATE TABLE dbo.__CaccMigraciones (
                Nombre nvarchar(200) NOT NULL PRIMARY KEY,
                Hash char(64) NOT NULL,
                AplicadaEnUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadHistoryAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        var history = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Nombre, Hash FROM dbo.__CaccMigraciones;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            history.Add(reader.GetString(0), reader.GetString(1));
        }
        return history;
    }

    private static async Task ApplyScriptAsync(SqlConnection connection, MigrationScript script, CancellationToken cancellationToken)
    {
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = 120;
            command.CommandText = script.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
            command.CommandText = "INSERT INTO dbo.__CaccMigraciones (Nombre, Hash) VALUES (@nombre, @hash);";
            command.Parameters.Add("@nombre", SqlDbType.NVarChar, 200).Value = script.Name;
            command.Parameters.Add("@hash", SqlDbType.Char, 64).Value = script.Hash;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Dispose rolls back the active transaction, even if the connection failed.
            throw new InvalidOperationException($"Falló la migración '{script.Name}'. La API no iniciará. Revisar el error SQL; los scripts anteriores exitosos permanecen aplicados.", ex);
        }
    }

    // Releases the applock explicitly; if it fails, disposing the non-pooled connection closes the session
    // anyway, so the original error (if any) is kept.
    private static async Task ReleaseLockAsync(SqlConnection connection, Action<Exception> onLockReleaseFailed)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = "EXEC sys.sp_releaseapplock @Resource = N'CACC:Migraciones', @LockOwner = 'Session';";
        try
        {
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqlException ex)
        {
            onLockReleaseFailed(ex);
        }
    }
}
