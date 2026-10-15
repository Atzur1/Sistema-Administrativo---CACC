using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ApiGestion.Reports
{
    // Un dato de la ficha del reporte (ej. "Período consultado: Septiembre 2026").
    // Highlight lo resalta en rojo, para el único valor que de verdad importa
    // destacar en cada reporte (ej. "Total adeudado").
    public readonly record struct PdfMetaItem(string Label, string Value, bool Highlight = false);

    // Una tarjeta de resumen (ej. "Total recaudado: $ 740.000"). Va arriba de la tabla, para que lo principal se vea
    // sin leer las filas. AccentColor es el color de su franja lateral y de su número.
    public sealed record PdfKpi(string Label, string Value, string? Detail = null, string AccentColor = "#007f3e");

    // Una parte de una barra de reparto (ej. efectivo vs transferencia). El peso es solo proporcional.
    public sealed record PdfShare(string Label, decimal Weight, string Color);

    // Colores de una etiqueta en forma de pastilla dentro de una columna (ej. el medio de pago).
    public sealed record PdfBadgeStyle(string Background, string Foreground);

    // Lo opcional de un reporte. Todo lo que no se indique queda como en la plantilla básica (tabla simple).
    public sealed record PdfReportExtras
    {
        // Fecha y hora de emisión, a la derecha del encabezado de cada página.
        public string? IssuedAt { get; init; }

        public IReadOnlyList<PdfKpi> Kpis { get; init; } = Array.Empty<PdfKpi>();

        // Barra de reparto bajo las tarjetas.
        public IReadOnlyList<PdfShare> Distribution { get; init; } = Array.Empty<PdfShare>();

        // Ancho relativo de cada columna de la tabla. Sin esto, todas valen lo mismo.
        public float[]? ColumnWeights { get; init; }

        // Columna cuyo valor se dibuja como pastilla de color, y el color de cada valor.
        public int? BadgeColumn { get; init; }
        public Func<string, PdfBadgeStyle?>? BadgeStyle { get; init; }

        // Fila de total al final de la tabla: el texto va a la izquierda y el valor en la última columna.
        public string? TotalLabel { get; init; }
        public string? TotalValue { get; init; }
    }

    // Plantilla común para todos los reportes PDF del club. Todos los reportes de "Reportes", HU-021 y Recaudación
    // Mensual la usan, así que el documento se ve igual venga de la pantalla que venga — nuevos reportes deberían
    // sumarse acá en vez de armar su propio Document.Create.
    //
    // Estructura: un encabezado fijo en cada página (logo, nombre del club, título, emisión), y en el contenido la
    // ficha de datos, las tarjetas de resumen, la tabla y el total.
    public static class PdfReportBuilder
    {
        private const string ClubName = "Club Atlético Camioneros de Córdoba";

        // Colores institucionales (los mismos del portal).
        private const string BrandGreen = "#007f3e";
        private const string BrandDark = "#0d4223";
        private const string BrandTint = "#eef7f1";
        private const string InkDark = "#1a1a1a";
        private const string InkMuted = "#5f6b64";
        private const string Line = "#dfe6e1";
        private const string Zebra = "#f6f9f7";

        // El escudo viaja dentro del ensamblado (ver ApiGestion.csproj): no depende de ninguna carpeta del servidor.
        private static readonly Lazy<byte[]?> Logo = new(() =>
        {
            using Stream? stream = typeof(PdfReportBuilder).Assembly.GetManifestResourceStream("ApiGestion.Reports.logo-cacc.png");
            if (stream is null) return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });

        public static byte[] Build(
            string title,
            string? subtitle,
            IReadOnlyList<PdfMetaItem> meta,
            string[] columns,
            IReadOnlyList<string[]> rows,
            string emptyMessage,
            ISet<int>? rightAlignedColumns = null,
            PdfReportExtras? extras = null)
            => BuildDocument(title, subtitle, meta, columns, rows, emptyMessage, rightAlignedColumns, extras).GeneratePdf();

        // El documento sin generar todavía: sirve para el PDF y para las pruebas (que lo dibujan como imagen).
        public static IDocument BuildDocument(
            string title,
            string? subtitle,
            IReadOnlyList<PdfMetaItem> meta,
            string[] columns,
            IReadOnlyList<string[]> rows,
            string emptyMessage,
            ISet<int>? rightAlignedColumns = null,
            PdfReportExtras? extras = null)
        {
            var rightAligned = rightAlignedColumns ?? new HashSet<int>();
            extras ??= new PdfReportExtras();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.MarginHorizontal(36);
                    page.MarginVertical(30);
                    // QuestPDF solo trae "Lato" empaquetada por default; cualquier otra
                    // fuente depende de que esté instalada en el server, no garantizado.
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Lato").FontColor(InkDark));

                    page.Header().Element(header => ComposeHeader(header, title, extras));
                    page.Content().PaddingTop(16).Element(content =>
                        ComposeContent(content, subtitle, meta, columns, rows, emptyMessage, rightAligned, extras));
                    page.Footer().Element(ComposeFooter);
                });
            });
        }

        // ===== Encabezado (se repite en todas las páginas) =====

        private static void ComposeHeader(IContainer container, string title, PdfReportExtras extras)
        {
            container.Column(column =>
            {
                column.Item().Row(row =>
                {
                    byte[]? logo = Logo.Value;
                    if (logo is not null)
                    {
                        row.ConstantItem(46).Height(46).Image(logo).FitArea();
                    }

                    row.RelativeItem().PaddingLeft(logo is not null ? 12 : 0).AlignMiddle().Column(text =>
                    {
                        text.Item().Text(ClubName).FontSize(15).Bold().FontColor(BrandDark);
                        text.Item().PaddingTop(1).Text(title).FontSize(11.5f).SemiBold().FontColor(BrandGreen);
                    });

                    if (!string.IsNullOrWhiteSpace(extras.IssuedAt))
                    {
                        row.AutoItem().AlignRight().AlignMiddle().Column(issued =>
                        {
                            issued.Item().AlignRight().Text("EMITIDO").FontSize(7).SemiBold().FontColor(InkMuted).LetterSpacing(0.08f);
                            issued.Item().AlignRight().Text(extras.IssuedAt).FontSize(9.5f).SemiBold();
                        });
                    }
                });

                // Trazo verde: separa la identidad del club del contenido del reporte.
                column.Item().PaddingTop(9).LineHorizontal(2).LineColor(BrandGreen);
            });
        }

        // ===== Contenido =====

        private static void ComposeContent(
            IContainer container,
            string? subtitle,
            IReadOnlyList<PdfMetaItem> meta,
            string[] columns,
            IReadOnlyList<string[]> rows,
            string emptyMessage,
            ISet<int> rightAligned,
            PdfReportExtras extras)
        {
            container.Column(column =>
            {
                column.Spacing(14);

                if (!string.IsNullOrWhiteSpace(subtitle))
                {
                    column.Item().Text(subtitle).FontSize(9.5f).Italic().FontColor(InkMuted);
                }

                if (meta.Count > 0)
                {
                    column.Item().Element(item => ComposeMeta(item, meta));
                }

                if (extras.Kpis.Count > 0)
                {
                    column.Item().Element(item => ComposeKpis(item, extras.Kpis));
                }

                if (extras.Distribution.Sum(part => part.Weight) > 0)
                {
                    column.Item().Element(item => ComposeDistribution(item, extras.Distribution));
                }

                column.Item().Element(item => ComposeTable(item, columns, rows, emptyMessage, rightAligned, extras));
            });
        }

        // La ficha de datos: cada dato con su rótulo chico arriba, en una grilla (hasta 3 por renglón) en vez de
        // una sola fila de texto corrido, que con varios datos se apretaba y partía las palabras.
        private static void ComposeMeta(IContainer container, IReadOnlyList<PdfMetaItem> meta)
        {
            int perRow = Math.Min(meta.Count, 3);

            container.Border(0.75f).BorderColor(Line).Background(BrandTint).Padding(12).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    for (int i = 0; i < perRow; i++) columns.RelativeColumn();
                });

                foreach (PdfMetaItem item in meta)
                {
                    table.Cell().PaddingVertical(3).PaddingRight(8).Column(cell =>
                    {
                        cell.Item().Text(item.Label.ToUpperInvariant()).FontSize(7).SemiBold().FontColor(InkMuted).LetterSpacing(0.08f);
                        var value = cell.Item().PaddingTop(1).Text(item.Value).FontSize(10.5f).SemiBold();
                        if (item.Highlight) value.FontColor("#b42318");
                    });
                }
            });
        }

        private static void ComposeKpis(IContainer container, IReadOnlyList<PdfKpi> kpis)
        {
            container.Row(row =>
            {
                row.Spacing(10);
                foreach (PdfKpi kpi in kpis)
                {
                    row.RelativeItem().Border(0.75f).BorderColor(Line).Row(card =>
                    {
                        card.ConstantItem(4).Background(kpi.AccentColor);
                        card.RelativeItem().Padding(10).Column(text =>
                        {
                            text.Item().Text(kpi.Label.ToUpperInvariant()).FontSize(7).SemiBold().FontColor(InkMuted).LetterSpacing(0.08f);
                            text.Item().PaddingTop(3).Text(kpi.Value).FontSize(16).Bold().FontColor(kpi.AccentColor);
                            if (!string.IsNullOrWhiteSpace(kpi.Detail))
                            {
                                text.Item().PaddingTop(2).Text(kpi.Detail).FontSize(8.5f).FontColor(InkMuted);
                            }
                        });
                    });
                }
            });
        }

        // Barra horizontal dividida en partes proporcionales, con su leyenda: se ve de un vistazo cómo se reparte.
        private static void ComposeDistribution(IContainer container, IReadOnlyList<PdfShare> shares)
        {
            decimal total = shares.Sum(part => part.Weight);

            container.Column(column =>
            {
                column.Item().Height(9).Row(bar =>
                {
                    foreach (PdfShare part in shares.Where(p => p.Weight > 0))
                    {
                        bar.RelativeItem((float)part.Weight).Background(part.Color);
                    }
                });

                column.Item().PaddingTop(5).Row(legend =>
                {
                    legend.Spacing(16);
                    foreach (PdfShare part in shares)
                    {
                        int percentage = (int)Math.Round(part.Weight * 100m / total);
                        legend.AutoItem().Text(text =>
                        {
                            text.Span("● ").FontColor(part.Color);
                            text.Span($"{part.Label} {percentage} %").FontSize(8.5f).FontColor(InkMuted);
                        });
                    }
                });
            });
        }

        private static void ComposeTable(
            IContainer container,
            string[] columns,
            IReadOnlyList<string[]> rows,
            string emptyMessage,
            ISet<int> rightAligned,
            PdfReportExtras extras)
        {
            container.Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    for (int i = 0; i < columns.Length; i++)
                    {
                        float weight = extras.ColumnWeights is not null && i < extras.ColumnWeights.Length ? extras.ColumnWeights[i] : 1f;
                        definition.RelativeColumn(weight);
                    }
                });

                // El encabezado se repite solo en cada página nueva.
                table.Header(header =>
                {
                    for (int i = 0; i < columns.Length; i++)
                    {
                        Align(header.Cell().Element(HeaderCell), rightAligned.Contains(i)).Text(columns[i]);
                    }
                });

                for (int r = 0; r < rows.Count; r++)
                {
                    string[] row = rows[r];
                    bool alternate = r % 2 == 1;

                    for (int i = 0; i < row.Length; i++)
                    {
                        IContainer cell = table.Cell().Element(c => BodyCell(c, alternate));
                        PdfBadgeStyle? badge = extras.BadgeColumn == i ? extras.BadgeStyle?.Invoke(row[i]) : null;

                        if (badge is not null)
                        {
                            cell.AlignLeft().Element(pill => pill
                                .Background(badge.Background).CornerRadius(8)
                                .PaddingVertical(2).PaddingHorizontal(8)
                                .Text(row[i]).FontSize(8.5f).SemiBold().FontColor(badge.Foreground));
                        }
                        else
                        {
                            Align(cell, rightAligned.Contains(i)).Text(row[i]);
                        }
                    }
                }

                if (rows.Count == 0)
                {
                    table.Cell().ColumnSpan((uint)columns.Length).Element(c => BodyCell(c, false)).AlignCenter()
                        .PaddingVertical(10).Text(emptyMessage).Italic().FontColor(InkMuted);
                }
                else if (!string.IsNullOrWhiteSpace(extras.TotalLabel) && columns.Length > 1)
                {
                    table.Cell().ColumnSpan((uint)(columns.Length - 1)).Element(TotalCell)
                        .Text(extras.TotalLabel).FontSize(10.5f).Bold().FontColor(BrandDark);
                    table.Cell().Element(TotalCell).AlignRight()
                        .Text(extras.TotalValue ?? "").FontSize(11.5f).Bold().FontColor(BrandGreen);
                }
            });
        }

        // ===== Pie (se repite en todas las páginas) =====

        private static void ComposeFooter(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().LineHorizontal(0.75f).LineColor(Line);
                column.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text($"{ClubName} · Sistema Administrativo").FontSize(8).FontColor(InkMuted);
                    row.AutoItem().Text(text =>
                    {
                        text.DefaultTextStyle(style => style.FontSize(8).FontColor(InkMuted));
                        text.Span("Página ");
                        text.CurrentPageNumber();
                        text.Span(" de ");
                        text.TotalPages();
                    });
                });
            });
        }

        private static IContainer Align(IContainer container, bool right) =>
            right ? container.AlignRight() : container;

        private static IContainer HeaderCell(IContainer container) => container
            .Background(BrandDark)
            .DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold().FontSize(9))
            .PaddingVertical(7).PaddingHorizontal(8);

        // Filas alternadas (cebra): ayudan a seguir un renglón de punta a punta sin regla ni cuadrícula.
        private static IContainer BodyCell(IContainer container, bool alternate) => container
            .Background(alternate ? Zebra : Colors.White)
            .BorderBottom(0.5f).BorderColor(Line)
            .PaddingVertical(6).PaddingHorizontal(8)
            .DefaultTextStyle(x => x.FontSize(9.5f));

        private static IContainer TotalCell(IContainer container) => container
            .Background(BrandTint)
            .BorderTop(1.5f).BorderColor(BrandGreen)
            .PaddingVertical(8).PaddingHorizontal(8);
    }
}
