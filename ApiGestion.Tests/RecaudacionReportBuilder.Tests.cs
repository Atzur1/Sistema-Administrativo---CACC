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
            new() { IdPago = 1, Jugador = "PRUEBA, MATIAS", Dni = "99000003", Categoria = "AFA 20067", Monto = 100000, MedioPago = "Transferencia" },
            new() { IdPago = 2, Jugador = "PRUEBA, SANTIAGO", Dni = "99000005", Categoria = "Pt preAFA 2015", Monto = 85000, MedioPago = "Efectivo" },
        }
    };

    [Fact]
    public void Csv_usa_punto_y_coma_y_montos_numericos_con_total()
    {
        string csv = Encoding.UTF8.GetString(RecaudacionReportBuilder.BuildCsv(Datos(), "Octubre 2026", "Todas las categorías"));

        Assert.Contains("Jugador;DNI;Categoría;Medio de pago;Monto ($)", csv);
        Assert.Contains("PRUEBA, MATIAS;99000003;AFA 20067;Transferencia;100000", csv.Replace("\"PRUEBA, MATIAS\"", "PRUEBA, MATIAS"));
        Assert.Contains("TOTAL RECAUDADO;8;740000", csv);
        Assert.Contains("Período;Octubre 2026", csv);
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
