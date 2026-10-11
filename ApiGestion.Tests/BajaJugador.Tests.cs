namespace ApiGestion.Tests;

using ApiGestion.Controllers;
using DaoLibrary;
using EntityLibrary;
using ServiceLibrary;

// Jugadores dados de baja: cómo se muestran sus cuotas y qué informa la baja.
public class BajaJugadorTests
{
    private static readonly DateTime Hoy = new(2026, 10, 10);

    private static CuotaMovimiento Pendiente(int mes, decimal saldo, bool canceladoPorBaja) => new()
    {
        IdPago = mes, Estado = false, MontoBase = 10_000m, MontoFinal = 10_000m, SaldoAjustado = saldo,
        FechaVencimiento = new DateTime(2026, mes, 1), MotivoBeneficio = "Becado", BeneficioCanceladoPorBaja = canceladoPorBaja
    };

    private static CuotaMovimiento Abono(int mes, decimal monto) => new()
    {
        IdPago = 100 + mes, Estado = true, MontoBase = 10_000m, MontoFinal = monto, FechaVencimiento = new DateTime(2026, mes, 1),
        FechaPago = new DateTime(2026, mes, 5), MetodoPago = "Efectivo", MotivoBeneficio = "Becado"
    };

    [Fact]
    public void UnaCuotaPendienteConSaldoYBecaCanceladaPorBaja_LlevaLaMarca()
    {
        var cuota = Assert.Single(PagosService.ConstruirEstadoDeCuotas(new[] { Pendiente(9, 5_000m, true) }, Hoy));

        Assert.Equal(EstadosCuota.Vencido, cuota.Estado);
        Assert.Equal(5_000m, cuota.SaldoPendiente);
        Assert.True(cuota.BeneficioCanceladoPorBaja);
    }

    [Fact]
    public void UnaCuotaCubiertaAlCienPorCiento_NoLlevaLaMarca()
    {
        var cuota = Assert.Single(PagosService.ConstruirEstadoDeCuotas(new[] { Pendiente(9, 0m, true) }, Hoy));

        Assert.True(cuota.CubiertaPorBeneficio);
        Assert.False(cuota.BeneficioCanceladoPorBaja);
    }

    // Regla del club: si el alumno pagó con la beca vigente y después se la cancelan (por baja o a mano), la cuota
    // sigue figurando PAGADA, porque pagó en tiempo y forma.
    [Fact]
    public void UnaCuotaPagadaConLaBecaVigente_SiguePagada()
    {
        var cuota = Assert.Single(PagosService.ConstruirEstadoDeCuotas(new[] { Abono(8, 5_000m) }, Hoy));

        Assert.Equal(EstadosCuota.Pagado, cuota.Estado);
        Assert.Equal(0m, cuota.SaldoPendiente);
        Assert.False(cuota.BeneficioCanceladoPorBaja);
    }

    [Fact]
    public void ElMensajeDeLaBaja_InformaCuotasQuitadasYBecasCanceladas()
    {
        var mensaje = JugadoresController.MensajeBaja(new ResultadoBajaJugador(BajaJugadorResultado.Ok, 2, 1));

        Assert.Contains("No se le emitirán más cuotas", mensaje);
        Assert.Contains("Se quitaron 2 cuota(s)", mensaje);
        Assert.Contains("Se cancelaron 1 beca(s)", mensaje);
    }

    [Fact]
    public void ElMensajeDeLaBaja_SinNadaQueQuitar_NoMencionaCuotasNiBecas()
    {
        var mensaje = JugadoresController.MensajeBaja(new ResultadoBajaJugador(BajaJugadorResultado.Ok));

        Assert.DoesNotContain("Se quitaron", mensaje);
        Assert.DoesNotContain("Se cancelaron", mensaje);
    }
}
