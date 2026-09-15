namespace ApiGestion.Models;

// Outbound contract: this is the only thing the API is willing to expose about a
// discount. Internal ids from the discount type table are not returned.
public class DiscountResponseDTO
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Category { get; set; } = "";

    // The reason, as it reads in the TIPO_DESCUENTO catalogue
    public string Type { get; set; } = "";

    // '%' or '$'. Tells the client which of the two values below to render.
    public string ValueType { get; set; } = "";

    // Only one of these carries a value; the other travels as null, matching the
    // rule that a benefit is either percentage based or a fixed amount.
    public decimal? Percentage { get; set; }
    public decimal? FixedAmount { get; set; }

    public string StartDate { get; set; } = "";

    // Empty when the benefit has no expiry date
    public string EndDate { get; set; } = "";

    public bool IsActive { get; set; }
}
