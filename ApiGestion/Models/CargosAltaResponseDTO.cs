namespace ApiGestion.Models;

// Contrato de integración v1. Dice solo SI se generó cada cargo: nunca montos (el Portal Deportivo no los recibe).
public sealed class CargosAltaResponseDTO
{
    public int IdJugador { get; init; }

    // true si en esta llamada se creó la inscripción; false si ya existía o su género no la paga.
    public bool InscripcionGenerada { get; init; }

    // true si en esta llamada se creó la cuota del mes; false si ya existía, si todavía no se generan las del mes
    // (antes del día 10 la recibe con el resto) o si no hay un arancel vigente.
    public bool CuotaGenerada { get; init; }
}
