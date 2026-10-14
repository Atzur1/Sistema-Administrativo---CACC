using EntityLibrary;

namespace ApiGestion.Models;

// Header metrics of "Cuotas y Pagos" (HU-019 adds the global debt and the debtors count).
public class PaymentSummaryResponseDTO
{
    public decimal RecaudadoAnioActual { get; set; }
    public int PagosDelMes { get; set; }
    public int CantidadPendientes { get; set; }
    public decimal DeudaGlobalTotal { get; set; }
    public int JugadoresMorosos { get; set; }

    public static PaymentSummaryResponseDTO FromEntity(ResumenPagos summary)
    {
        return new PaymentSummaryResponseDTO
        {
            RecaudadoAnioActual = summary.RecaudadoAnioActual,
            PagosDelMes = summary.PagosDelMes,
            CantidadPendientes = summary.CantidadPendientes,
            DeudaGlobalTotal = summary.DeudaGlobalTotal,
            JugadoresMorosos = summary.JugadoresMorosos
        };
    }
}
