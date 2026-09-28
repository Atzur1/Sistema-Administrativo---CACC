namespace ApiGestion.Models;

// Outbound shape of a registered player, with the charges its registration
// generated (null when that charge was not generated).
public class PlayerResponseDTO
{
    public long Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Dni { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string Gender { get; set; } = "";
    public long CategoryId { get; set; }
    public string JoinDate { get; set; } = "";
    public decimal? MonthlyFeeAmount { get; set; }
    public decimal? EnrollmentFeeAmount { get; set; }
}
