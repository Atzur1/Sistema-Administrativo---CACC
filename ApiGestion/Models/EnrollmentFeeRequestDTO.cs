namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

// Inbound contract to schedule a new enrollment fee (HU-033). Each gender has its own fee; an amount of 0
// means that gender is not charged an enrollment from that date.
public class EnrollmentFeeRequestDTO
{
    [Required(ErrorMessage = "Indicá a qué género corresponde la inscripción.")]
    [RegularExpression("^(Masculino|Femenino)$", ErrorMessage = "El género debe ser Masculino o Femenino.")]
    public string? Gender { get; set; }

    [Required(ErrorMessage = "Indicá el monto.")]
    // Limits parsed as invariant: with the es-AR culture of the server "0" and "0.01" are not always numbers
    [Range(typeof(decimal), "0", "99999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "El monto no puede ser negativo (usá 0 si no se cobra inscripción).")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Indicá la fecha de inicio.")]
    public DateTime? StartDate { get; set; }
}
