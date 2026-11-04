namespace ApiGestion.Tests;

using System.Text;
using ApiGestion.Reports;
using EntityLibrary;
using QuestPDF.Fluent;

// HU-018: las exportaciones de Recaudación Mensual (PDF con logo y resumen, CSV listo para Excel en español).
public class RecaudacionReportBuilderTests
{
    public RecaudacionReportBuilderTests()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    private static RecaudacionPeriodo Datos() => new()
    {
        Total = 740000, Efectivo = 235000, Transferencia = 505000,
        Pagos = 8, PagosEfectivo = 3, PagosTransferencia = 5,
        Detalle = new List<PagoDelPeriodo>
        {
            new() { IdPago = 1, Jugador = "PRUEBA, MATIAS", Dni = "99000003", Categoria = "AFA 20067", Monto = 100000, MedioPago = "Transferencia",
                    FechaPago = new DateTime(2026, 10, 31), FechaHoraRegistro = new DateTime(2026, 10, 31, 14, 35, 12) },
            // Pago anterior a fecha_hora_registro: solo tiene el día.
            new() { IdPago = 2, Jugador = "PRUEBA, SANTIAGO", Dni = "99000005", Categoria = "Pt preAFA 2015", Monto = 85000, MedioPago = "Efectivo",
                    FechaPago = new DateTime(2026, 10, 2) },
        }
    };

    [Theory]
    [InlineData(7749000)]
    [InlineData(999999999)]
    public void Pdf_total_millonario_se_renderiza_completo_en_un_solo_texto(decimal total)
    {
        var datos = Datos();
        datos.Total = total;
        var svg = RecaudacionReportBuilder.BuildDocumento(datos, "Octubre 2026", "Todas las categorías").GenerateSvg().Last();
        string esperado = total.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("es-AR"));
        var documento = System.Xml.Linq.XDocument.Parse(svg);
        Assert.Contains(documento.Descendants().Where(e => e.Name.LocalName == "text"), e => e.Value.Trim() == esperado && (string?)e.Attribute("fill") == "#007F3E");
    }
    [Fact]
    public void Csv_usa_punto_y_coma_y_montos_numericos_con_total()
    {
        string csv = Encoding.UTF8.GetString(RecaudacionReportBuilder.BuildCsv(Datos(), "Octubre 2026", "Todas las categorías"));

        Assert.Contains("Fecha;Jugador;DNI;Categoría;Medio de pago;Monto ($)", csv);
        Assert.Contains("2026-10-31 14:35;PRUEBA, MATIAS;99000003;AFA 20067;Transferencia;100000", csv.Replace("\"PRUEBA, MATIAS\"", "PRUEBA, MATIAS"));
        Assert.Contains("TOTAL RECAUDADO;8;740000", csv);
        Assert.Contains("Período;Octubre 2026", csv);
    }

    // HU-107: la fecha va en ISO (no depende de la configuración regional) y el encabezado aclara la zona.
    [Fact]
    public void Csv_incluye_la_fecha_de_cada_cobro_y_la_zona_del_club()
    {
        string csv = Encoding.UTF8.GetString(RecaudacionReportBuilder.BuildCsv(Datos(), "Octubre 2026", "Todas las categorías"));

        Assert.Contains($"Fechas;Hora del club ({RelojNegocio.Zona})", csv);
        Assert.Contains("2026-10-02;PRUEBA, SANTIAGO;99000005", csv);
    }

    // HU-107: se exporta lo que recibe el builder, que es el período completo (no la página de la pantalla).
    [Fact]
    public void Csv_exporta_todas_las_filas_del_periodo()
    {
        var datos = Datos();
        for (int i = 3; i <= 130; i++)
        {
            datos.Detalle.Add(new PagoDelPeriodo { IdPago = i, Jugador = $"JUGADOR {i}", Monto = 1000, MedioPago = "Efectivo", FechaPago = new DateTime(2026, 10, 1 + i % 28) });
        }

        string[] lineas = Encoding.UTF8.GetString(RecaudacionReportBuilder.BuildCsv(datos, "Octubre 2026", "Todas las categorías")).Split('\n');
        int encabezado = Array.FindIndex(lineas, l => l.StartsWith("Fecha;Jugador"));

        Assert.Equal(130, lineas.Skip(encabezado + 1).Count(l => l.Trim().Length > 0));
    }

    [Fact]
    public void Pdf_se_genera_con_y_sin_cobros()
    {
        byte[] conCobros = RecaudacionReportBuilder.BuildPdf(Datos(), "Octubre 2026", "Todas las categorías");
        byte[] vacio = RecaudacionReportBuilder.BuildPdf(new RecaudacionPeriodo(), "Octubre 2026", "Todas las categorías");

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(conCobros, 0, 4));
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(vacio, 0, 4));
    }

    [Fact]
    public void Pdf_se_dibuja_como_imagen_para_revision_visual()
    {
        string? carpeta = Environment.GetEnvironmentVariable("CACC_PDF_PREVIEW");
        if (string.IsNullOrEmpty(carpeta)) return;

        var datos = Datos();
        for (int i = 0; i < 30; i++) datos.Detalle.Add(datos.Detalle[i % 2]);
        var imagenes = RecaudacionReportBuilder.BuildDocumento(datos, "Octubre 2026", "Todas las categorías").GenerateImages().ToList();
        for (int i = 0; i < imagenes.Count; i++) File.WriteAllBytes(Path.Combine(carpeta, $"recaudacion-{i + 1}.png"), imagenes[i]);
    }
}
