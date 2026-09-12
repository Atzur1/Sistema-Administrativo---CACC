namespace ApiGestion.Models;

// Outbound contract: this is the only thing the API is willing to expose about a
// discount. Internal ids from the discount type table are not returned.
public class DiscountResponseDTO
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Category { get; set; } = "";
    public string Type { get; set; } = "";
    public int Percentage { get; set; }
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";
    public bool IsActive { get; set; }
}
