using System.Globalization;
using EntityLibrary;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace ApiGestion.Reports
{
    // HU-018: planilla de la recaudación de un período, con el desglose por medio
    // de pago arriba y el detalle de cobros abajo. Misma fuente que el panel de
    // Recaudación Mensual (RecaudacionDao), así que lo exportado siempre coincide
    // con lo que se ve en pantalla.
    public static class RecaudacionReportBuilder
    {
        private static readonly CultureInfo Ars = CultureInfo.GetCultureInfo("es-AR");

        private const string ColorEfectivo = "#007f3e";
        private const string ColorTransferencia = "#2563eb";

        private static readonly string[] Columnas =
            { "Jugador", "DNI", "Categoría", "Medio de pago", "Monto" };

        // Jugador y categoría necesitan más lugar que el DNI o el medio de pago.
        private static readonly float[] Anchos = { 3.2f, 1.5f, 2.3f, 1.8f, 1.7f };

        // "6 pagos · 75 %". Con total cero el porcentaje no se puede calcular,
        // así que se informa el vacío en vez de dividir por cero.
        private static string Participacion(int pagos, decimal monto, decimal total)
        {
            if (total == 0)
            {
                return "Sin cobros";
            }
            // Porcentaje del dinero (no de la cantidad de cobros): coincide con la barra de reparto.
            int porcentaje = (int)Math.Round((monto * 100m) / total);
            return $"{pagos} {(pagos == 1 ? "pago" : "pagos")} · {porcentaje} %";
        }

        private static string Pesos(decimal monto) => monto.ToString("C0", Ars);

        // El CSV usa punto y coma (Excel en español lo abre en columnas) y los montos como número puro, para que
        // se puedan sumar y ordenar. Quedan al final, en filas aparte, los totales.
        public static byte[] BuildCsv(RecaudacionPeriodo datos, string periodoLabel, string categoriaLabel)
        {
            var meta = new List<string[]>
            {
                new[] { "Club Atlético Camioneros de Córdoba" },
                new[] { "Reporte de Recaudación Mensual" },
                new[] { "Emitido", RelojNegocio.Ahora.ToString("dd/MM/yyyy HH:mm") },
                new[] { "Período", periodoLabel },
                new[] { "Categoría", categoriaLabel },
                Array.Empty<string>(),
                new[] { "RESUMEN", "Cobros", "Monto ($)" },
                new[] { "Efectivo", datos.PagosEfectivo.ToString(), datos.Efectivo.ToString("0", Ars) },
                new[] { "Transferencia", datos.PagosTransferencia.ToString(), datos.Transferencia.ToString("0", Ars) },
                new[] { "TOTAL RECAUDADO", datos.Pagos.ToString(), datos.Total.ToString("0", Ars) },
            };

            var rows = datos.Detalle.Select(p => new[]
            {
                p.Jugador,
                p.Dni,
                p.Categoria,
                p.MedioPago,
                p.Monto.ToString("0", Ars),
            }).ToList();

            string[] columnas = { "Jugador", "DNI", "Categoría", "Medio de pago", "Monto ($)" };
            return CsvBuilder.Build(meta, columnas, rows, separator: ';');
        }

        public static byte[] BuildPdf(RecaudacionPeriodo datos, string periodoLabel, string categoriaLabel)
            => BuildDocumento(datos, periodoLabel, categoriaLabel).GeneratePdf();

        internal static IDocument BuildDocumento(RecaudacionPeriodo datos, string periodoLabel, string categoriaLabel)
        {
            var meta = new List<PdfMetaItem>
            {
                new("Período", periodoLabel),
                new("Categoría", categoriaLabel),
                new("Cobros procesados", datos.Pagos.ToString()),
            };

            var kpis = new List<PdfKpi>
            {
                new("Total recaudado", Pesos(datos.Total), $"{datos.Pagos} {(datos.Pagos == 1 ? "cobro" : "cobros")}", "#0d4223"),
                new("Efectivo", Pesos(datos.Efectivo), Participacion(datos.PagosEfectivo, datos.Efectivo, datos.Total), ColorEfectivo),
                new("Transferencia", Pesos(datos.Transferencia), Participacion(datos.PagosTransferencia, datos.Transferencia, datos.Total), ColorTransferencia),
            };

            var reparto = new List<PdfShare>
            {
                new("Efectivo", datos.Efectivo, ColorEfectivo),
                new("Transferencia", datos.Transferencia, ColorTransferencia),
            };

            var rows = datos.Detalle.Select(p => new[]
            {
                p.Jugador,
                p.Dni,
                p.Categoria,
                p.MedioPago,
                Pesos(p.Monto),
            }).ToList();

            var extras = new PdfReportExtras
            {
                IssuedAt = RelojNegocio.Ahora.ToString("dd/MM/yyyy HH:mm"),
                Kpis = kpis,
                Distribution = reparto,
                ColumnWeights = Anchos,
                BadgeColumn = 3,
                BadgeStyle = medio => medio switch
                {
                    "Efectivo" => new PdfBadgeStyle("#dff3e7", "#0d4223"),
                    "Transferencia" => new PdfBadgeStyle("#dbe8fd", "#1e40af"),
                    _ => null,
                },
                TotalLabel = $"Total recaudado ({datos.Pagos} {(datos.Pagos == 1 ? "cobro" : "cobros")})",
                TotalValue = Pesos(datos.Total),
            };

            return PdfReportBuilder.BuildDocument(
                title: "Reporte de Recaudación Mensual",
                subtitle: null,
                meta: meta,
                columns: Columnas,
                rows: rows,
                emptyMessage: "No se registraron cobros en este período.",
                rightAlignedColumns: new HashSet<int> { 4 },
                extras: extras);
        }
    }
}
