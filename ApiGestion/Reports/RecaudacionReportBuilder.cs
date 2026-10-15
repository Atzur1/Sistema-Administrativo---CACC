using System.Globalization;
using EntityLibrary;

namespace ApiGestion.Reports
{
    // HU-018: planilla de la recaudación de un período, con el desglose por medio
    // de pago arriba y el detalle de cobros abajo. Misma fuente que el panel de
    // Recaudación Mensual (RecaudacionDao), así que lo exportado siempre coincide
    // con lo que se ve en pantalla.
    public static class RecaudacionReportBuilder
    {
        private static readonly CultureInfo Ars = CultureInfo.GetCultureInfo("es-AR");

        private static readonly string[] Columnas =
            { "Jugador", "DNI", "Categoría", "Medio de pago", "Monto" };

        // "6 pagos · $145.000 (75%)". Con cero cobros el porcentaje no se puede
        // calcular, así que se informa el vacío en vez de dividir por cero.
        private static string Linea(int pagos, decimal monto, int totalPagos)
        {
            if (totalPagos == 0)
            {
                return "Sin cobros";
            }
            int porcentaje = (int)Math.Round((pagos * 100m) / totalPagos);
            return $"{pagos} pagos · {monto.ToString("C0", Ars)} ({porcentaje}%)";
        }

        public static byte[] BuildCsv(RecaudacionPeriodo datos, string periodoLabel, string categoriaLabel)
        {
            var meta = new List<string[]>
            {
                new[] { "Club Atlético Camioneros de Córdoba" },
                new[] { "Reporte de Recaudación Mensual" },
                new[] { "Fecha de emisión", RelojNegocio.Ahora.ToString("dd/MM/yyyy HH:mm") },
                new[] { "Período consultado", periodoLabel },
                new[] { "Categoría", categoriaLabel },
                new[] { "Total recaudado", datos.Total.ToString("C0", Ars) },
                new[] { "Efectivo", Linea(datos.PagosEfectivo, datos.Efectivo, datos.Pagos) },
                new[] { "Transferencia", Linea(datos.PagosTransferencia, datos.Transferencia, datos.Pagos) },
                new[] { "Cobros procesados", datos.Pagos.ToString() },
            };

            var rows = datos.Detalle.Select(p => new[]
            {
                p.Jugador,
                p.Dni,
                p.Categoria,
                p.MedioPago,
                p.Monto.ToString("C0", Ars),
            });

            return CsvBuilder.Build(meta, Columnas, rows);
        }

        public static byte[] BuildPdf(RecaudacionPeriodo datos, string periodoLabel, string categoriaLabel)
        {
            var meta = new List<PdfMetaItem>
            {
                new("Fecha de emisión", RelojNegocio.Ahora.ToString("dd/MM/yyyy HH:mm")),
                new("Período consultado", periodoLabel),
                new("Categoría consultada", categoriaLabel),
                new("Cobros procesados", datos.Pagos.ToString()),
                new("Efectivo", Linea(datos.PagosEfectivo, datos.Efectivo, datos.Pagos)),
                new("Transferencia", Linea(datos.PagosTransferencia, datos.Transferencia, datos.Pagos)),
                new("Total recaudado", datos.Total.ToString("C0", Ars), Highlight: true),
            };

            var rows = datos.Detalle.Select(p => new[]
            {
                p.Jugador,
                p.Dni,
                p.Categoria,
                p.MedioPago,
                p.Monto.ToString("C0", Ars),
            }).ToList();

            return PdfReportBuilder.Build(
                title: "Reporte de Recaudación Mensual",
                subtitle: "Liquidez del período y desglose por medio de pago",
                meta: meta,
                columns: Columnas,
                rows: rows,
                emptyMessage: "No se registraron cobros en este período.",
                rightAlignedColumns: new HashSet<int> { 4 });
        }
    }
}
