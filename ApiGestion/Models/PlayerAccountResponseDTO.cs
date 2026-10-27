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

    // Overdue ones (from the 10th of the following month): 1 -> training only, 2+ -> suspended
    public int OverdueInstallments { get; set; }

    // "yyyy-MM-dd", or null while the player is active.
    public string? LeaveDate { get; set; }

    // The sport status computed by the backend (habilitado, solo_entrenamientos, inhabilitado or baja): the same rule and the
    // same texts as the integration API. The screen shows it; it does not recompute it.
    public string SportStatus { get; set; } = EntityLibrary.EstadoDeportivoTexto.Habilitado;
}
