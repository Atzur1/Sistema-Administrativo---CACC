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

    // Both mandatory since HU-012, in yyyy-MM-dd
    public string StartDate { get; set; } = "";
    public string EndDate { get; set; } = "";

    // Where the benefit stands today: "Scheduled", "Active" or "Expired".
    //
    // The server resolves it against its own clock and it is the only thing the
    // client has to read to know the situation. It is not stored anywhere, so a
    // benefit expires by itself the day its range ends, with no daily job and no
    // button for the administrator to press.
    public string Status { get; set; } = "";

    // Derived from Status, kept for the consumers HU-014 left behind. It is not
    // a second source of truth: the mapper fills it from Status and nothing
    // else writes it.
    public bool IsActive { get; set; }
}
