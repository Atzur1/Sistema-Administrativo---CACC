using System.Data;
using Microsoft.Data.SqlClient;

namespace ApiGestion.Database;

public static class EjecutorMigraciones
{
    public static async Task EjecutarAsync(string conexion, ILogger logger, CancellationToken cancellationToken)
    {
        const string prefijo = "ApiGestion.Database.Migrations.";
        var assembly = typeof(EjecutorMigraciones).Assembly;
        var scripts = new List<Migracion>();
        foreach (var recurso in assembly.GetManifestResourceNames().Where(x => x.StartsWith(prefijo, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(recurso)!;
            using var reader = new StreamReader(stream);
            scripts.Add(Migracion.Crear(recurso[prefijo.Length..], await reader.ReadToEndAsync(cancellationToken)));
        }

        // Validar todo antes de abrir una conexión o efectuar cambios.
        PlanMigraciones.Pendientes(scripts, new Dictionary<string, string>());
        // Una conexión dedicada sin pool cierra la sesión incluso si se cancela la adquisición
        // del bloqueo; nunca deja un applock retenido en una conexión reutilizable.
        var opcionesConexion = new SqlConnectionStringBuilder(conexion) { Pooling = false };
        await using var db = new SqlConnection(opcionesConexion.ConnectionString);
        await db.OpenAsync(cancellationToken);
        // El bloqueo de sesión abarca historial y todos los scripts. Al cerrar se libera.
        await using var bloqueo = db.CreateCommand();
        bloqueo.CommandTimeout = 70;
        bloqueo.CommandText = """
            DECLARE @resultado int;
            EXEC @resultado = sys.sp_getapplock
                @Resource = N'CACC:Migraciones', @LockMode = 'Exclusive',
                @LockOwner = 'Session', @LockTimeout = 60000;
            SELECT @resultado;
            """;
        var resultado = Convert.ToInt32(await bloqueo.ExecuteScalarAsync(cancellationToken));
        if (resultado < 0)
            throw new InvalidOperationException($"No se pudo obtener el bloqueo de migraciones (código {resultado}). Otra instancia puede estar migrando; volver a intentar.");

        try
        {
            await using var crearHistorial = db.CreateCommand();
            crearHistorial.CommandText = """
                SET XACT_ABORT ON;
                IF OBJECT_ID(N'dbo.__CaccMigraciones', N'U') IS NULL
                CREATE TABLE dbo.__CaccMigraciones (
                    Nombre nvarchar(200) NOT NULL PRIMARY KEY,
                    Hash char(64) NOT NULL,
                    AplicadaEnUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
                );
                """;
            await crearHistorial.ExecuteNonQueryAsync(cancellationToken);
            var historial = new Dictionary<string, string>(StringComparer.Ordinal);
            await using (var leer = db.CreateCommand())
            {
                leer.CommandText = "SELECT Nombre, Hash FROM dbo.__CaccMigraciones;";
                await using var reader = await leer.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    historial.Add(reader.GetString(0), reader.GetString(1));
            }

            var pendientes = PlanMigraciones.Pendientes(scripts, historial);
            foreach (var script in pendientes)
            {
                await using var transaccion = (SqlTransaction)await db.BeginTransactionAsync(cancellationToken);
                try
                {
                    await using var comando = db.CreateCommand();
                    comando.Transaction = transaccion;
                    comando.CommandTimeout = 120;
                    comando.CommandText = script.Sql;
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                    comando.CommandText = "INSERT INTO dbo.__CaccMigraciones (Nombre, Hash) VALUES (@nombre, @hash);";
                    comando.Parameters.Add("@nombre", SqlDbType.NVarChar, 200).Value = script.Nombre;
                    comando.Parameters.Add("@hash", SqlDbType.Char, 64).Value = script.Hash;
                    await comando.ExecuteNonQueryAsync(cancellationToken);
                    await transaccion.CommitAsync(cancellationToken);
                    logger.LogInformation("Migración aplicada: {Migracion}", script.Nombre);
                }
                catch (Exception ex)
                {
                    // Dispose revierte la transacción activa, incluso si la conexión falló.
                    throw new InvalidOperationException($"Falló la migración '{script.Nombre}'. La API no iniciará. Revisar el error SQL; los scripts anteriores exitosos permanecen aplicados.", ex);
                }
            }
            logger.LogInformation("Migraciones verificadas. Scripts aplicados en este inicio: {Cantidad}", pendientes.Count);
        }
        finally
        {
            // Pooling puede mantener la sesión física abierta: liberar explícitamente el applock.
            if (db.State == ConnectionState.Open)
            {
                await using var liberar = db.CreateCommand();
                liberar.CommandText = "EXEC sys.sp_releaseapplock @Resource = N'CACC:Migraciones', @LockOwner = 'Session';";
                try
                {
                    await liberar.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch (SqlException ex)
                {
                    // Dispose cierra la sesión sin pool; conservar el error original si lo hubo.
                    logger.LogWarning(ex, "No se pudo liberar explícitamente el bloqueo; se cerrará la sesión de migraciones.");
                }
            }
        }
    }
}
