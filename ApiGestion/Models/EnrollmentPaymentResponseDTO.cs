namespace ApiGestion.Models;

public class EnrollmentPaymentResponseDTO
{
    public long Id { get; set; }
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = "";
    public string PaymentDate { get; set; } = "";
}
