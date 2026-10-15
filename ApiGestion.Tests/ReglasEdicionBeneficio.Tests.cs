namespace ApiGestion.Tests;

using EntityLibrary;
using ServiceLibrary;

// Una bonificación cubre cuotas enteras, así que lo que ya transcurrió son las cuotas ya emitidas: una
// cerrada no se toca, de una en curso solo se mueve hasta qué cuota rige (sin dejar sin beneficio una cuota ya
// emitida), y una programada no puede pasar a empezar con una cuota que ya no se puede becar.
public class ReglasEdicionBeneficioTests
{
    // 10 de octubre de 2026: justo hoy se emite la cuota de octubre, la última emitida.
    private static readonly DateTime Hoy = new(2026, 10, 10);
    private static readonly DateTime PrimeraPermitida = new(2026, 10, 1);

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
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2026, 12, 31)), Hoy, PrimeraPermitida));
    }

    [Fact]
    public void UnaEnCurso_PuedeExtenderOAcortarSuFinHastaLaUltimaCuotaEmitida()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 3, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.Permitida,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2027, 6, 30)), Hoy, PrimeraPermitida));
        // Octubre es la última cuota emitida: se puede cortar justo ahí (31/10), no antes.
        Assert.Equal(ResultadoEdicionBeneficio.Permitida,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2026, 10, 31)), Hoy, PrimeraPermitida));
    }

    [Fact]
    public void UnaEnCurso_NoPuedeDejarSinBeneficioUnaCuotaYaEmitida()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 3, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.FechaFinEnElPasado,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: new DateTime(2026, 9, 30)), Hoy, PrimeraPermitida));
        // Una fecha a mitad de octubre se lleva a fin de mes recién en el servicio; acá, sin llevar, sigue siendo antes.
        Assert.Equal(ResultadoEdicionBeneficio.FechaFinEnElPasado,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, fin: Hoy), Hoy, PrimeraPermitida));
    }

    [Fact]
    public void UnaEnCurso_NoCambiaValorMotivoNiInicio()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 3, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.SoloFechaFin, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, porcentaje: 100), Hoy, PrimeraPermitida));
        Assert.Equal(ResultadoEdicionBeneficio.SoloFechaFin, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, motivo: "Descuento"), Hoy, PrimeraPermitida));
        Assert.Equal(ResultadoEdicionBeneficio.SoloFechaFin, ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 1, 1)), Hoy, PrimeraPermitida));
    }

    // Las bonificaciones cargadas antes de la regla de cuotas enteras pueden empezar a mitad de mes: el pedido
    // llega llevado al día 1, y eso no cuenta como cambiar el inicio.
    [Fact]
    public void UnaEnCursoVieja_ConInicioAMitadDeMes_SigueEditandoSoloElFin()
    {
        var actual = Beneficio(DiscountStatus.Active, new DateTime(2026, 9, 27), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.Permitida,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 9, 1), fin: new DateTime(2027, 3, 31)), Hoy, PrimeraPermitida));
    }

    [Fact]
    public void UnaProgramada_SeEditaCompletaPeroNoEmpiezaAntesDeLaPrimeraCuotaQueSePuedeBecar()
    {
        var actual = Beneficio(DiscountStatus.Scheduled, new DateTime(2026, 11, 1), new DateTime(2026, 12, 31));

        Assert.Equal(ResultadoEdicionBeneficio.Permitida,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, porcentaje: 100, inicio: new DateTime(2026, 12, 1)), Hoy, PrimeraPermitida));
        Assert.Equal(ResultadoEdicionBeneficio.InicioEnElPasado,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 9, 1)), Hoy, PrimeraPermitida));
    }

    // Si la cuota de octubre ya tiene pagos, la primera que se puede becar es noviembre: octubre ya no.
    [Fact]
    public void UnaProgramada_NoPuedeAdelantarseAUnaCuotaQueYaTienePagos()
    {
        var actual = Beneficio(DiscountStatus.Scheduled, new DateTime(2026, 12, 1), new DateTime(2027, 3, 31));
        var primeraConOctubrePagada = new DateTime(2026, 11, 1);

        Assert.Equal(ResultadoEdicionBeneficio.InicioEnElPasado,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 10, 1)), Hoy, primeraConOctubrePagada));
        Assert.Equal(ResultadoEdicionBeneficio.Permitida,
            ReglasEdicionBeneficio.Evaluar(actual, Copia(actual, inicio: new DateTime(2026, 11, 1)), Hoy, primeraConOctubrePagada));
    }
}
