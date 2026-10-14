using EntityLibrary;

namespace ApiGestion.Models;

// Banner of "Actividad y Movimientos": payments and amount collected today.
public class TodayPaymentSummaryResponseDTO
{
    public int PagosHoy { get; set; }
    public decimal RecaudadoHoy { get; set; }

    public static TodayPaymentSummaryResponseDTO FromEntity(ResumenPagosHoy summary)
    {
        return new TodayPaymentSummaryResponseDTO
        {
            PagosHoy = summary.PagosHoy,
            RecaudadoHoy = summary.RecaudadoHoy
        };
    }
}
