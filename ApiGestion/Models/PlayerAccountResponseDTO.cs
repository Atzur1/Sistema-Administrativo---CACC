namespace ApiGestion.Models;

// Outbound contract of the payment roster (HU-029): what the treasury screen needs to chase a
// debt and nothing else.
public class PlayerAccountResponseDTO
{
    public int PlayerId { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Dni { get; set; } = "";
    public string Category { get; set; } = "";

    // 0 when the player is up to date
    public decimal AmountOwed { get; set; }
    public int PendingInstallments { get; set; }
}
