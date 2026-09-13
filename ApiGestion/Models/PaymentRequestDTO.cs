namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

// Inbound contract to register a payment. The payment date and the user who
// registers it are never taken from the client: the server sets both.
public class PaymentRequestDTO
{
    [Range(1, long.MaxValue, ErrorMessage = "The player id must be greater than zero.")]
    public long PlayerId { get; set; }

    [Range(2000, 2100, ErrorMessage = "The period year must be between 2000 and 2100.")]
    public int PeriodYear { get; set; }

    [Range(1, 12, ErrorMessage = "The period month must be between 1 and 12.")]
    public int PeriodMonth { get; set; }

    [Range(0.01, double.MaxValue, ErrorMessage = "The amount must be greater than zero.")]
    public decimal Amount { get; set; }

    [Required(ErrorMessage = "The payment method is required.")]
    public string Method { get; set; } = "";

    [StringLength(50, ErrorMessage = "The reference cannot exceed 50 characters.")]
    public string? Reference { get; set; }
}
