namespace ApiGestion.Models;

// Outbound contract for a tariff, whether currently in force, scheduled ahead,
// or already replaced by a later one.
public class TariffResponseDTO
{
    public long Id { get; set; }
    public string Branch { get; set; } = "";
    public decimal Amount { get; set; }
    public string ValidFrom { get; set; } = "";
    public string? ValidTo { get; set; }
    public bool IsActive { get; set; }
}
