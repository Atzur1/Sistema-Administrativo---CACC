using System.Text;

namespace ApiGestion.Reports
{
    // Escritor de CSV mínimo para los reportes de exportación (HU-021 y las cajas
    // de la pantalla Reportes): unas líneas de encabezado institucional, una fila
    // en blanco, y la tabla de columnas + datos. No se trajo una librería para
    // algo así de simple.
    public static class CsvBuilder
    {
        public static byte[] Build(IEnumerable<string[]> metaRows, string[] columns, IEnumerable<string[]> dataRows)
        {
            var sb = new StringBuilder();
            var meta = metaRows.ToList();

            foreach (var row in meta)
            {
                sb.AppendLine(string.Join(",", row.Select(Escape)));
            }
            if (meta.Count > 0)
            {
                sb.AppendLine();
            }

            sb.AppendLine(string.Join(",", columns.Select(Escape)));
            foreach (var row in dataRows)
            {
                sb.AppendLine(string.Join(",", row.Select(Escape)));
            }

            // BOM UTF-8: sin esto Excel abre acentos y "ñ" rotos en Windows.
            var bom = new byte[] { 0xEF, 0xBB, 0xBF };
            return bom.Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        private static string Escape(string? value)
        {
            value ??= "";
            return value.Contains(',') || value.Contains('"') || value.Contains('\n')
                ? $"\"{value.Replace("\"", "\"\"")}\""
                : value;
        }
    }
}
