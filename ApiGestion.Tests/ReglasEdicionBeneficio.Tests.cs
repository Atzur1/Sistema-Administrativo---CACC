namespace ApiGestion.Tests;

using EntityLibrary;
using ServiceLibrary;

// Lo que ya transcurrió de una beca o descuento no se edita: una cerrada no se toca, de una en curso
// solo se mueve la fecha de fin, y una programada no puede pasar a empezar en el pasado.
public class ReglasEdicionBeneficioTests
{
    private static readonly DateTime Hoy = new(2026, 10, 10);

    private static Discount Beneficio(DiscountStatus estado, DateTime inicio, DateTime fin, decimal porcentaje = 50) => new()
    {
        Type = "Becado", ValueType = "%", Percentage = porcentaje, StartDate = inicio, EndDate = fin, Status = estado
    };

    private static Discount Copia(Discount d, DateTime? inicio = null, DateTime? fin = null, decimal? porcentaje = null, string? motivo = null) => new()
    {
        Type = motivo ?? d.Type, ValueType = d.ValueType, Percentage = porcentaje ?? d.Percentage,
        StartDate = inicio ?? d.StartDate, EndDate = fin ?? d.EndDate, Status = d.Status
    };

    [Theory]
    [InlineData(DiscountStatus.Expired)]
    [InlineData(DiscountStatus.Cancelled)]
    [InlineData(DiscountStatus.Voided)]
    public void UnaBonificacionCerrada_NoSeEditaNiSiquieraLaFechaDeFin(DiscountStatus estado)
    {
        var actual = Beneficio(estado, new DateTime(2026, 1, 1), new DateTime(2026, 6, 30));

        Assert.Equal(ResultadoEdicionBeneficio.Cerrada,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2026, 12, 31)), Hoy));
    }

    [Fact]
    public void UnaEnCurso_PuedeExtenderOAcortarSuFin()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 3, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.Permitida, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2027, 6, 30)), Hoy));
        Assert.Equal(ResultadoEdicionBeneficio.Permitida, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: Hoy), Hoy));
    }

    [Fact]
    public void UnaEnCurso_NoPuedeTerminarAntesDeHoy()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 3, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.FechaFinEnElPasado,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2026, 9, 30)), Hoy));
    }

    [Fact]
    public void UnaEnCurso_NoCambiaValorMotivoNiInicio()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 3, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.SoloFechaFin, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, porcentaje: 100), Hoy));
        Assert.Equal(ResultadoEdicionBeneficio.SoloFechaFin, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, motivo: "Descuento"), Hoy));
        Assert.Equal(ResultadoEdicionBeneficio.SoloFechaFin, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 1, 1)), Hoy));
    }

    [Fact]
    public void UnaProgramada_SeEditaCompletaPeroNoEmpiezaEnElPasado()
    {
        var actual = Beneficio(DiscountStatus.Scheduled, new DateTime(2026, 11, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.Permitida,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, porcentaje: 100, inicio: new DateTime(2026, 12, 1)), Hoy));
        Assert.Equal(ResultadoEdicionBeneficio.InicioEnElPasado,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 9, 1)), Hoy));
    }
}
