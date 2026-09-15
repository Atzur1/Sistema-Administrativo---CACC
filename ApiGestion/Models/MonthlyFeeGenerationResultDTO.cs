namespace ApiGestion.Models;

// Outbound summary of a monthly fee generation run.
public class MonthlyFeeGenerationResultDTO
{
    public int TotalGenerated { get; set; }
    public int TotalSkipped { get; set; }

    // e.g. "Septiembre 2026", built server-side so the client never guesses
    // which period the defaulted month/year resolved to.
    public string PeriodName { get; set; } = "";
}
