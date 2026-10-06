namespace ApiGestion.Tests;

using EntityLibrary;
using ServiceLibrary;

// HU-024: estado de cuenta del jugador. Las reglas de cada cuota (Pendiente/Vencido/Pagado,
// beneficios, abonos) son las de ConstruirEstadoDeCuotas, ya cubiertas en PagosService.Cobro.Tests;
// acá se prueba lo que agrega el estado de cuenta: el total a abonar, el orden y los montos congelados.
public class PagosServiceStatementTests
{
    private static readonly DateTime Hoy = new(2026, 4, 15);

    private static CuotaMovimiento Movimiento(int id, int mes, bool pagado, decimal montoBase, decimal? saldo = null,
        DateTime? fechaPago = null) => new()
    {
        IdPago = id,
        Estado = pagado,
        MontoBase = montoBase,
        MontoFinal = montoBase,
        SaldoAjustado = saldo ?? montoBase,
        FechaVencimiento = new DateTime(2026, mes, 1),
        FechaPago = fechaPago,
        MetodoPago = pagado ? "Efectivo" : null
    };

    private static PlayerStatementAccount Cuenta(params CuotaMovimiento[] movimientos) => new()
    {
        PlayerId = 12,
        FirstName = "NICOLÁS",
        LastName = "BAZÁN",
        Dni = "47970803",
        Movements = movimientos
    };

    [Fact]
    public void Total_AddsPendienteAndVencido_ButNotPagado()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(
            Movimiento(1, 1, pagado: true, 70_000m, fechaPago: new DateTime(2026, 1, 10)), // Pagado
            Movimiento(2, 3, pagado: false, 85_000m),                                       // Vencido
            Movimiento(3, 4, pagado: false, 85_000m)),                                      // Pendiente
            Hoy);

        Assert.Equal(new[] { EstadosCuota.Pagado, EstadosCuota.Vencido, EstadosCuota.Pendiente }, estado.Fees.Select(f => f.Estado));
        Assert.Equal(170_000m, estado.TotalDebtAmount);
    }

    [Fact]
    public void Total_UsesTheBalanceAfterBenefitsAndPartialPayments()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(
            Movimiento(2, 3, pagado: false, 85_000m, saldo: 42_500m), // beneficio del 50%
            Movimiento(3, 4, pagado: false, 85_000m, saldo: 0m)),     // becado al 100%
            Hoy);

        Assert.Equal(42_500m, estado.TotalDebtAmount);
        Assert.True(estado.Fees[1].CubiertaPorBeneficio);
    }

    [Fact]
    public void FullyPaidPlayer_HasNoDebt()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(
            Movimiento(1, 1, pagado: true, 70_000m, fechaPago: new DateTime(2026, 1, 10)),
            Movimiento(2, 2, pagado: true, 70_000m, fechaPago: new DateTime(2026, 2, 10))),
            Hoy);

        Assert.Equal(0m, estado.TotalDebtAmount);
        Assert.All(estado.Fees, f => Assert.Equal(EstadosCuota.Pagado, f.Estado));
    }

    [Fact]
    public void PlayerWithoutFees_HasAnEmptyStatementAndNoDebt()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(), Hoy);

        Assert.Empty(estado.Fees);
        Assert.Equal(0m, estado.TotalDebtAmount);
        Assert.Equal("BAZÁN", estado.LastName);
    }

    [Fact]
    public void Fees_AreChronological_EvenIfTheRowsArriveOutOfOrder()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(
            Movimiento(3, 4, pagado: false, 85_000m),
            Movimiento(1, 1, pagado: false, 70_000m),
            Movimiento(2, 3, pagado: false, 85_000m)),
            Hoy);

        Assert.Equal(new[] { "Enero 2026", "Marzo 2026", "Abril 2026" }, estado.Fees.Select(f => f.Periodo));
    }

    // Regla §2.3: cada cuota conserva el monto con que se emitió, aunque el arancel cambie después.
    [Fact]
    public void EachFee_KeepsTheFrozenAmountItWasIssuedWith()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(
            Movimiento(1, 1, pagado: false, 70_000m),
            Movimiento(2, 3, pagado: false, 85_000m)),
            Hoy);

        Assert.Equal(new[] { 70_000m, 85_000m }, estado.Fees.Select(f => f.MontoCuota));
        Assert.Equal(155_000m, estado.TotalDebtAmount);
    }

    [Fact]
    public void Header_ComesFromTheAccount()
    {
        PlayerStatement estado = PagosService.BuildPlayerStatement(Cuenta(), Hoy);

        Assert.Equal(12, estado.PlayerId);
        Assert.Equal("NICOLÁS", estado.FirstName);
        Assert.Equal("47970803", estado.Dni);
    }
}
