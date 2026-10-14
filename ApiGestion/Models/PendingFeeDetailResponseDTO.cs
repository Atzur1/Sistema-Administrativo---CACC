using EntityLibrary;

namespace ApiGestion.Models;

// A fee the player still owes, with what was already paid and what is left.
public class PendingFeeDetailResponseDTO
{
    public int IdPago { get; set; }
    public string Periodo { get; set; } = "";
    public string Concepto { get; set; } = "";
    public decimal MontoOriginal { get; set; }

    // With the Becados y Descuentos benefit already applied (never negative)
    public decimal SaldoPendiente { get; set; }
    public bool TieneBeneficio { get; set; }
    public string? MotivoBeneficio { get; set; }
    public string? TipoValorBeneficio { get; set; }
    public decimal? PorcentajeBeneficio { get; set; }
    public decimal? MontoFijoBeneficio { get; set; }
    public bool BeneficioCanceladoPorBaja { get; set; }
    public List<PartialPaymentDetailDTO> Abonos { get; set; } = new();

    public static PendingFeeDetailResponseDTO FromEntity(CuotaPendienteDetalle fee)
    {
        return new PendingFeeDetailResponseDTO
        {
            IdPago = fee.IdPago,
            Periodo = fee.Periodo,
            Concepto = fee.Concepto,
            MontoOriginal = fee.MontoOriginal,
            SaldoPendiente = fee.SaldoPendiente,
            TieneBeneficio = fee.TieneBeneficio,
            MotivoBeneficio = fee.MotivoBeneficio,
            TipoValorBeneficio = fee.TipoValorBeneficio,
            PorcentajeBeneficio = fee.PorcentajeBeneficio,
            MontoFijoBeneficio = fee.MontoFijoBeneficio,
            BeneficioCanceladoPorBaja = fee.BeneficioCanceladoPorBaja,
            Abonos = fee.Abonos.Select(a => new PartialPaymentDetailDTO
            {
                Monto = a.Monto,
                MetodoPago = a.MetodoPago,
                FechaPago = a.FechaPago
            }).ToList()
        };
    }
}

public class PartialPaymentDetailDTO
{
    public decimal Monto { get; set; }
    public string MetodoPago { get; set; } = "";
    public DateTime FechaPago { get; set; }
}
