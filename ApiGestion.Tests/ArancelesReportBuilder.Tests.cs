namespace ApiGestion.Tests;

using System.Text;
using ApiGestion.Reports;
using EntityLibrary;

// El historial de aranceles ya no habla de "Vigente hasta" (parece un vencimiento, y un arancel que
// nadie reemplaza se sigue cobrando todos los meses): muestra desde cuándo lo reemplaza el siguiente.
public class ArancelesReportBuilderTests
{
    private static ArancelHistorialItem Item(DateTime? vigenteHasta) => new()
    {
        IdArancel = 1,
        Genero = "Masculino",
        Monto = 85000,
        VigenteDesde = new DateTime(2026, 9, 1),
        VigenteHasta = vigenteHasta,
        Estado = vigenteHasta == null ? "Vigente" : "Anterior"
    };

    private static string Csv(params ArancelHistorialItem[] items) =>
        Encoding.UTF8.GetString(ArancelesReportBuilder.BuildCsv(items));

    [Fact]
    public void Csv_UsesReplacedOnInsteadOfValidUntil()
    {
        string csv = Csv(Item(null));

        Assert.Contains("Reemplazado el", csv);
        Assert.DoesNotContain("Vigente hasta", csv);
    }

    [Fact]
    public void Csv_ShowsTheDateTheNextFeeStartsNotTheLastDayOfTheOldOne()
    {
        // El anterior rigió hasta el 30/09; el siguiente lo reemplaza el 01/10.
        string csv = Csv(Item(new DateTime(2026, 9, 30)));

        Assert.Contains("01/10/2026", csv);
        Assert.DoesNotContain("30/09/2026", csv);
    }

    [Fact]
    public void Csv_ShowsADashForAFeeNobodyHasReplaced()
    {
        string csv = Csv(Item(null));

        Assert.Contains("—", csv);
    }
}
