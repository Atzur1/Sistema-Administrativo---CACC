namespace ApiGestion.Models;

// Outbound shape of an enrollment fee. Dates travel as yyyy-MM-dd, like the
// benefits of Becados y Descuentos.
public class EnrollmentFeeResponseDTO
{
    public long Id { get; set; }
    public decimal Amount { get; set; }
    public string StartDate { get; set; } = "";
    public string? EndDate { get; set; }
    public string Status { get; set; } = "";
}
