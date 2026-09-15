namespace ApiGestion.Models;

// Outbound contract for a fee, whether already paid or still pending.
public class PaymentResponseDTO
{
    public long Id { get; set; }
    public long PlayerId { get; set; }
    public string PlayerName { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal Amount { get; set; }
    public string? PaymentDate { get; set; }
    public string? DueDate { get; set; }
    public string? Period { get; set; }
    public string Method { get; set; } = "";
    public bool IsPaid { get; set; }
    public string? Reference { get; set; }
    public string? RegisteredAt { get; set; }
}
