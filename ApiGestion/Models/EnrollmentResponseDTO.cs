namespace ApiGestion.Models;

// Outbound shape of a player's enrollment fee and its payments (HU-033)
public class EnrollmentResponseDTO
{
    public long PlayerId { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal PendingBalance { get; set; }
    public string DueDate { get; set; } = "";
    public List<EnrollmentPaymentResponseDTO> Payments { get; set; } = new();
}
