namespace ApiGestion.Models;

// Outbound contract for the counters on the Cuotas y Pagos header.
public class TreasuryMetricsResponseDTO
{
    public decimal CollectedThisYear { get; set; }
    public int PaymentsThisMonth { get; set; }
    public int PendingCount { get; set; }
}
