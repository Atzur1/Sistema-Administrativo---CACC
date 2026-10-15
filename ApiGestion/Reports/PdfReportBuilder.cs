using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace ApiGestion.Reports
{
    // Un dato del encabezado institucional (ej. "Fecha de emisión: 21/09/2026").
    // Highlight lo resalta en rojo, para el único valor que de verdad importa
    // destacar en cada reporte (ej. "Total adeudado").
    public readonly record struct PdfMetaItem(string Label, string Value, bool Highlight = false);

    // Plantilla común para todos los reportes PDF del club: título + subtítulo
    // opcional + una línea de metadatos, y una tabla de datos con encabezado en
    // verde institucional. Todos los reportes de "Reportes" y HU-021 la usan,
    // así que el documento se ve igual venga de la pantalla que venga — nuevos
    // reportes deberían sumarse acá en vez de armar su propio Document.Create.
    public static class PdfReportBuilder
    {
        public static byte[] Build(
            string title,
            string? subtitle,
            IReadOnlyList<PdfMetaItem> meta,
            string[] columns,
            IReadOnlyList<string[]> rows,
            string emptyMessage,
            ISet<int>? rightAlignedColumns = null)
        {
            var rightAligned = rightAlignedColumns ?? new HashSet<int>();

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(32);
                    // QuestPDF solo trae "Lato" empaquetada por default; cualquier otra
                    // fuente depende de que esté instalada en el server, no garantizado.
                    page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Lato"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text("Club Atlético Camioneros de Córdoba").FontSize(16).Bold();
                        col.Item().PaddingTop(2).Text(title).FontSize(12).SemiBold().FontColor(Colors.Green.Darken2);

                        if (!string.IsNullOrWhiteSpace(subtitle))
                        {
                            col.Item().PaddingTop(1).Text(subtitle).FontSize(9.5f).FontColor(Colors.Grey.Darken2);
                        }

                        if (meta.Count > 0)
                        {
                            col.Item().PaddingTop(10).Row(row =>
                            {
                                foreach (var item in meta)
                                {
                                    row.RelativeItem().Text(t =>
                                    {
                                        t.Span($"{item.Label}: ").SemiBold();
                                        var span = t.Span(item.Value);
                                        if (item.Highlight)
                                        {
                                            span.FontColor(Colors.Red.Darken2).SemiBold();
                                        }
                                    });
                                }
                            });
                        }

                        col.Item().PaddingTop(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten1);
                    });

                    page.Content().PaddingTop(16).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            foreach (var _ in columns)
                            {
                                cols.RelativeColumn();
                            }
                        });

                        table.Header(header =>
                        {
                            for (int i = 0; i < columns.Length; i++)
                            {
                                Align(header.Cell().Element(HeaderCell), rightAligned.Contains(i)).Text(columns[i]);
                            }
                        });

                        foreach (var row in rows)
                        {
                            for (int i = 0; i < row.Length; i++)
                            {
                                Align(table.Cell().Element(BodyCell), rightAligned.Contains(i)).Text(row[i]);
                            }
                        }

                        if (rows.Count == 0)
                        {
                            table.Cell().ColumnSpan((uint)columns.Length).Element(BodyCell).AlignCenter()
                                .Text(emptyMessage).Italic();
                        }
                    });

                    page.Footer().AlignCenter().Text(t =>
                    {
                        t.Span("Página ");
                        t.CurrentPageNumber();
                        t.Span(" de ");
                        t.TotalPages();
                    });
                });
            }).GeneratePdf();
        }

        private static IContainer Align(IContainer container, bool right) =>
            right ? container.AlignRight() : container;

        private static IContainer HeaderCell(IContainer container) => container
            .Background(Colors.Green.Darken2)
            .DefaultTextStyle(x => x.FontColor(Colors.White).SemiBold())
            .PaddingVertical(6).PaddingHorizontal(6);

        private static IContainer BodyCell(IContainer container) => container
            .BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            .PaddingVertical(5).PaddingHorizontal(6);
    }
}
