namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

// Inbound contract to schedule a new enrollment fee (HU-033)
public class EnrollmentFeeRequestDTO
{
    [Required(ErrorMessage = "Indicá el monto.")]
    // Limits parsed as invariant: with the es-AR culture of the server "0.01" is not a number
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "El monto debe ser mayor a cero.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Indicá la fecha de inicio.")]
    public DateTime? StartDate { get; set; }
}
