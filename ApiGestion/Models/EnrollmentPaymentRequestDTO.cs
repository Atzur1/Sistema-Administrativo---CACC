namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;
using EntityLibrary;

// Inbound contract to pay, fully or partially, a player's enrollment fee (HU-033)
public class EnrollmentPaymentRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "The amount is required.")]
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "The amount must be greater than zero.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "The payment method is required.")]
    public string PaymentMethod { get; set; } = "";

    // Same payment methods monthly fees accept
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(PaymentMethod) && !MetodosPago.EsValido(PaymentMethod.Trim()))
        {
            yield return new ValidationResult(
                $"The payment method must be one of: {string.Join(", ", MetodosPago.Validos)}.",
                new[] { nameof(PaymentMethod) });
        }
    }
}
