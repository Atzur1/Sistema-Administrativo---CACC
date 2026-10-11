using DaoLibrary;

namespace ApiGestion.Database;

// Reads the scripts embedded in the API and decides which ones are missing (PlanMigraciones). Everything that
// touches the database (lock, history and running each script) lives in DatabaseMigrationDao.
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

        // Validate everything before opening a connection or making changes.
        PlanMigraciones.Pendientes(scripts, new Dictionary<string, string>());

        var migrationDao = new DatabaseMigrationDao(conexion);
        int aplicadas = await migrationDao.ApplyPendingMigrationsAsync(
            historial => PlanMigraciones.Pendientes(scripts, historial)
                .Select(m => new MigrationScript(m.Nombre, m.Sql, m.Hash))
                .ToList(),
            nombre => logger.LogInformation("Migración aplicada: {Migracion}", nombre),
            ex => logger.LogWarning(ex, "No se pudo liberar explícitamente el bloqueo; se cerrará la sesión de migraciones."),
            cancellationToken);
        logger.LogInformation("Migraciones verificadas. Scripts aplicados en este inicio: {Cantidad}", aplicadas);
    }
}
