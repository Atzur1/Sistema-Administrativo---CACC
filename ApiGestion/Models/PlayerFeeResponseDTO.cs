using EntityLibrary;

namespace ApiGestion.Models;

// HU-025: a monthly fee of the player with its real status (Pendiente, Vencido or Pagado).
public class PlayerFeeResponseDTO
{
    public int IdPago { get; set; }
    public string Periodo { get; set; } = "";
    public DateTime FechaVencimiento { get; set; }
    public decimal MontoCuota { get; set; }
    public decimal SaldoPendiente { get; set; }
    public decimal MontoAbonado { get; set; }
    public string Estado { get; set; } = "";
    public bool CubiertaPorBeneficio { get; set; }
    public string? MotivoBeneficio { get; set; }
    public bool BeneficioCanceladoPorBaja { get; set; }
    public string? MetodoPago { get; set; }
    public DateTime? FechaPago { get; set; }

    public static PlayerFeeResponseDTO FromEntity(CuotaJugador fee)
    {
        return new PlayerFeeResponseDTO
        {
            IdPago = fee.IdPago,
            Periodo = fee.Periodo,
            FechaVencimiento = fee.FechaVencimiento,
            MontoCuota = fee.MontoCuota,
            SaldoPendiente = fee.SaldoPendiente,
            MontoAbonado = fee.MontoAbonado,
            Estado = fee.Estado,
            CubiertaPorBeneficio = fee.CubiertaPorBeneficio,
            MotivoBeneficio = fee.MotivoBeneficio,
            BeneficioCanceladoPorBaja = fee.BeneficioCanceladoPorBaja,
            MetodoPago = fee.MetodoPago,
            FechaPago = fee.FechaPago
        };
    }
}
