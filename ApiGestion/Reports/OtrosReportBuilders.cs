using System.Globalization;
using EntityLibrary;

namespace ApiGestion.Reports
{
    // Reportes CSV de las cajas de la pantalla "Reportes" (Cuotas y Pagos,
    // Becados y Descuentos, Actualización de Aranceles). El de deudores vive
    // aparte en DeudoresReportBuilder porque HU-021 también le pide PDF.
    public static class PagosRecientesReportBuilder
    {
        private static readonly CultureInfo Ars = CultureInfo.GetCultureInfo("es-AR");

        public static byte[] BuildCsv(IReadOnlyList<PagoReciente> pagos)
        {
            var meta = new List<string[]>
            {
                new[] { "Club Atlético Camioneros de Córdoba" },
                new[] { "Reporte de Pagos Recibidos" },
                new[] { "Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm") },
                new[] { "Total recaudado (listado)", pagos.Sum(p => p.Monto).ToString("C0", Ars) },
            };

            var columns = new[] { "Jugador", "Método de pago", "Monto", "Fecha de pago" };

            var rows = pagos.Select(p => new[]
            {
                p.NombreCompleto,
                p.MetodoPago,
                p.Monto.ToString("C0", Ars),
                p.FechaPago.ToString("dd/MM/yyyy"),
            });

            return CsvBuilder.Build(meta, columns, rows);
        }

        public static byte[] BuildPdf(IReadOnlyList<PagoReciente> pagos)
        {
            var meta = new List<PdfMetaItem>
            {
                new("Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm")),
                new("Total recaudado (listado)", pagos.Sum(p => p.Monto).ToString("C0", Ars), Highlight: true),
            };

            var columns = new[] { "Jugador", "Método de pago", "Monto", "Fecha de pago" };

            var rows = pagos.Select(p => new[]
            {
                p.NombreCompleto,
                p.MetodoPago,
                p.Monto.ToString("C0", Ars),
                p.FechaPago.ToString("dd/MM/yyyy"),
            }).ToList();

            return PdfReportBuilder.Build(
                title: "Reporte de Pagos Recibidos",
                subtitle: "Historial de pagos registrados en el sistema",
                meta: meta,
                columns: columns,
                rows: rows,
                emptyMessage: "No hay pagos registrados.",
                rightAlignedColumns: new HashSet<int> { 2 });
        }
    }

    public static class BecadosReportBuilder
    {
        // El código de estado viaja en inglés (DiscountStatus); acá se traduce
        // igual que en la pantalla (statusLabel en DiscountModel.ts), para que el
        // reporte hable el mismo idioma que la grilla.
        private static readonly Dictionary<string, string> EstadoLabels = new()
        {
            ["Scheduled"] = "Programada",
            ["Active"] = "Activa",
            ["Expired"] = "Expirada",
            ["Cancelled"] = "Cancelada",
        };

        // "0.##" en vez de "N0"/valor crudo: igual que formatBenefitValue en
        // DiscountModel.ts (maximumFractionDigits: 2, sin ceros de más), así
        // "50%" no sale como "50.00%" en el reporte.
        private static string FormatValor(Discount b) => b.ValueType == "%"
            ? $"{b.Percentage?.ToString("0.##")}%"
            : $"${b.FixedAmount?.ToString("N0")}";

        public static byte[] BuildCsv(IReadOnlyList<Discount> beneficios)
        {
            var meta = new List<string[]>
            {
                new[] { "Club Atlético Camioneros de Córdoba" },
                new[] { "Reporte de Becados y Descuentos" },
                new[] { "Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm") },
                new[] { "Beneficios listados", beneficios.Count.ToString() },
            };

            var columns = new[] { "Jugador", "Categoría", "Motivo", "Valor", "Vigencia desde", "Vigencia hasta", "Estado" };

            var rows = beneficios.Select(b => new[]
            {
                b.PlayerName,
                b.Category,
                b.Type,
                FormatValor(b),
                b.StartDate.ToString("dd/MM/yyyy"),
                b.EndDate.ToString("dd/MM/yyyy"),
                EstadoLabels.GetValueOrDefault(b.Status.ToString(), b.Status.ToString()),
            });

            return CsvBuilder.Build(meta, columns, rows);
        }

        public static byte[] BuildPdf(IReadOnlyList<Discount> beneficios)
        {
            var meta = new List<PdfMetaItem>
            {
                new("Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm")),
                new("Beneficios listados", beneficios.Count.ToString()),
            };

            var columns = new[] { "Jugador", "Categoría", "Motivo", "Valor", "Vigencia desde", "Vigencia hasta", "Estado" };

            var rows = beneficios.Select(b => new[]
            {
                b.PlayerName,
                b.Category,
                b.Type,
                FormatValor(b),
                b.StartDate.ToString("dd/MM/yyyy"),
                b.EndDate.ToString("dd/MM/yyyy"),
                EstadoLabels.GetValueOrDefault(b.Status.ToString(), b.Status.ToString()),
            }).ToList();

            return PdfReportBuilder.Build(
                title: "Reporte de Becados y Descuentos",
                subtitle: "Beneficios asignados, vigentes y no vigentes",
                meta: meta,
                columns: columns,
                rows: rows,
                emptyMessage: "No hay beneficios asignados.",
                rightAlignedColumns: new HashSet<int> { 3 });
        }
    }

    public static class ArancelesReportBuilder
    {
        private static readonly CultureInfo Ars = CultureInfo.GetCultureInfo("es-AR");

        public static byte[] BuildCsv(IReadOnlyList<ArancelHistorialItem> historial)
        {
            var meta = new List<string[]>
            {
                new[] { "Club Atlético Camioneros de Córdoba" },
                new[] { "Reporte de Actualización de Aranceles" },
                new[] { "Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm") },
            };

            var columns = new[] { "Género", "Monto", "Vigente desde", "Vigente hasta", "Estado" };

            var rows = historial.Select(a => new[]
            {
                a.Genero,
                a.Monto.ToString("C0", Ars),
                a.VigenteDesde.ToString("dd/MM/yyyy"),
                a.VigenteHasta?.ToString("dd/MM/yyyy") ?? "—",
                a.Estado,
            });

            return CsvBuilder.Build(meta, columns, rows);
        }

        public static byte[] BuildPdf(IReadOnlyList<ArancelHistorialItem> historial)
        {
            var meta = new List<PdfMetaItem>
            {
                new("Fecha de emisión", DateTime.Now.ToString("dd/MM/yyyy HH:mm")),
            };

            var columns = new[] { "Género", "Monto", "Vigente desde", "Vigente hasta", "Estado" };

            var rows = historial.Select(a => new[]
            {
                a.Genero,
                a.Monto.ToString("C0", Ars),
                a.VigenteDesde.ToString("dd/MM/yyyy"),
                a.VigenteHasta?.ToString("dd/MM/yyyy") ?? "—",
                a.Estado,
            }).ToList();

            return PdfReportBuilder.Build(
                title: "Reporte de Actualización de Aranceles",
                subtitle: "Historial de aranceles por género: vigentes, programados y anteriores",
                meta: meta,
                columns: columns,
                rows: rows,
                emptyMessage: "No hay aranceles registrados.",
                rightAlignedColumns: new HashSet<int> { 1 });
        }
    }
}
