namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

// Inbound contract to trigger the monthly fee generation. Both fields are
// optional: when omitted, the stored procedure defaults to the current
// server month/year (the "mes vigente" the HU is about).
public class MonthlyFeeGenerationRequestDTO
{
    [Range(1, 12, ErrorMessage = "The period month must be between 1 and 12.")]
    public int? PeriodMonth { get; set; }

    [Range(2000, 2100, ErrorMessage = "The period year must be between 2000 and 2100.")]
    public int? PeriodYear { get; set; }
}
