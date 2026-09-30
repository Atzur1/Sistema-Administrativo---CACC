using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ApiGestion.Database;

public sealed record Migracion(string Nombre, string Version, string Sql, string Hash)
{
    public static Migracion Crear(string nombre, string sql)
    {
        var coincidencia = Regex.Match(nombre, @"^V(?<version>\d{8}_\d{2})__[a-z0-9_]+\.sql$");
        if (!coincidencia.Success || nombre.Length > 200)
            throw new InvalidOperationException($"Migración '{nombre}': usar VAAAAMMDD_NN__descripcion.sql (máximo 200 caracteres).");

        // Git puede convertir CRLF a LF: no representa un cambio de contenido.
        sql = sql.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        // Contrato deliberadamente conservador: un lote, sin SQLCMD ni control de transacciones.
        // También rechaza estas expresiones dentro de comentarios/literales; ver database/README.md.
        if (string.IsNullOrWhiteSpace(sql) ||
            Regex.IsMatch(sql, @"^\s*(GO\b|:|!!)", RegexOptions.Multiline | RegexOptions.IgnoreCase) ||
            sql.Contains("$(", StringComparison.Ordinal) ||
            Regex.IsMatch(sql, @"\b(COMMIT|ROLLBACK|USE)\b|\b(BEGIN|SAVE)\s+(DISTRIBUTED\s+)?TRAN(SACTION)?\b|\bSET\s+(NOEXEC|PARSEONLY|IMPLICIT_TRANSACTIONS|XACT_ABORT)\b", RegexOptions.IgnoreCase))
            throw new InvalidOperationException($"Migración '{nombre}': SQL vacío o formato no soportado. Usar un único lote, sin GO, SQLCMD, USE ni control de transacciones/sesión.");

        return new(nombre, coincidencia.Groups["version"].Value, sql,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
    }
}

public static class PlanMigraciones
{
    public static IReadOnlyList<Migracion> Pendientes(IEnumerable<Migracion> scripts,
        IReadOnlyDictionary<string, string> historial)
    {
        var ordenadas = scripts.OrderBy(x => x.Version, StringComparer.Ordinal).ToArray();
        if (ordenadas.GroupBy(x => x.Version).Any(x => x.Count() > 1))
            throw new InvalidOperationException("Hay versiones de migración duplicadas. Asignar una versión única antes de aplicarlas.");

        var porNombre = ordenadas.ToDictionary(x => x.Nombre, StringComparer.Ordinal);
        foreach (var (nombre, hash) in historial)
        {
            if (!porNombre.TryGetValue(nombre, out var script))
                throw new InvalidOperationException($"La migración aplicada '{nombre}' no está en esta versión del código. Actualizar la rama; no borrar el historial.");
            if (!string.Equals(script.Hash, hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"La migración aplicada '{nombre}' fue modificada. Restaurarla y crear un script nuevo para la corrección.");
        }

        var ultima = ordenadas.LastOrDefault(x => historial.ContainsKey(x.Nombre));
        var pendientes = ordenadas.Where(x => !historial.ContainsKey(x.Nombre)).ToArray();
        if (ultima is not null && pendientes.Any(x => string.CompareOrdinal(x.Version, ultima.Version) < 0))
            throw new InvalidOperationException("Hay migraciones pendientes anteriores a la última aplicada. Renumerar únicamente los scripts todavía no aplicados.");
        return pendientes;
    }
}
