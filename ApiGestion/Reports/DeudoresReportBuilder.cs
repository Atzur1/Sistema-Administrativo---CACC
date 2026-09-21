using System.Globalization;
using EntityLibrary;

namespace ApiGestion.Reports
{
    // HU-021: planilla imprimible/descargable de jugadores deudores. Misma fuente
    // de datos que el padrón de "Deudas y Morosidad" (PendienteJugador, ya con el
    // saldo congelado y el beneficio de Becados y Descuentos aplicado), así que
    // lo que se exporta siempre coincide con lo que se ve en pantalla.
    public static class DeudoresReportBuilder
    {
        private static readonly CultureInfo Ars = CultureInfo.GetCultureInfo("es-AR");

        public static byte[] BuildCsv(IReadOnlyList<PendienteJugador> deudores, string categoriaLabel)
        {
            var totalAdeudado = deudores.Sum(d => d.MontoTotal);

            var meta = new List<string[]>
            {
                new[] { "Club Atlético Camioneros de Córdoba" },
                new[] { "Reporte de Jugadores Deudores" },
                new[] { "Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm") },
                new[] { "Categoría", categoriaLabel },
                new[] { "Total adeudado", totalAdeudado.ToString("C0", Ars) },
            };

            var columns = new[] { "Nombre y Apellido", "DNI", "Categoría", "Meses Adeudados", "Total a Abonar Actualizado" };

            var rows = deudores.Select(d => new[]
            {
                d.NombreCompleto,
                d.Dni,
                d.Categoria,
                d.CantidadCuotas.ToString(),
                d.MontoTotal.ToString("C0", Ars),
            });

            return CsvBuilder.Build(meta, columns, rows);
        }

        public static byte[] BuildPdf(IReadOnlyList<PendienteJugador> deudores, string categoriaLabel)
        {
            var totalAdeudado = deudores.Sum(d => d.MontoTotal);

            var meta = new List<PdfMetaItem>
            {
                new("Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm")),
                new("Categoría consultada", categoriaLabel),
                new("Total adeudado", totalAdeudado.ToString("C0", Ars), Highlight: true),
            };

            var columns = new[] { "Nombre y Apellido", "DNI", "Categoría", "Meses Adeudados", "Total a Abonar Actualizado" };

            var rows = deudores.Select(d => new[]
            {
                d.NombreCompleto,
                d.Dni,
                d.Categoria,
                d.CantidadCuotas.ToString(),
                d.MontoTotal.ToString("C0", Ars),
            }).ToList();

            return PdfReportBuilder.Build(
                title: "Reporte de Jugadores Deudores",
                subtitle: "Planilla de respaldo para auditorías de tesorería y gestión de cobranza",
                meta: meta,
                columns: columns,
                rows: rows,
                emptyMessage: "No hay jugadores deudores para este filtro.",
                rightAlignedColumns: new HashSet<int> { 3, 4 });
        }
    }
}
