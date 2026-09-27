namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

// Inbound contract to schedule a new enrollment fee (HU-033)
public class EnrollmentFeeRequestDTO
{
    [Required(ErrorMessage = "The amount is required.")]
    // Limits parsed as invariant: with the es-AR culture of the server "0.01" is not a number
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "The amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "The start date is required.")]
    public DateTime? StartDate { get; set; }
}
