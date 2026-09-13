namespace ApiGestion.Models;

// Inbound contract to schedule a new tariff for one branch. It never
// references an existing fee or payment: scheduling only opens a new validity
// period going forward.
public class ScheduleTariffRequestDTO
{
    public string Branch { get; set; } = "";
    public decimal Amount { get; set; }
    public string ValidFrom { get; set; } = "";
}
