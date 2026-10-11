namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;
using EntityLibrary;

// Inbound contract to pay, fully or partially, a player's enrollment fee (HU-033)
public class EnrollmentPaymentRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "Indicá el monto.")]
    [Range(typeof(decimal), "0.01", "99999999", ParseLimitsInInvariantCulture = true,
        ErrorMessage = "El monto debe ser mayor a cero.")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Indicá el método de pago.")]
    public string PaymentMethod { get; set; } = "";

    // Same payment methods monthly fees accept
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(PaymentMethod) && !MetodosPago.EsValido(PaymentMethod.Trim()))
        {
            yield return new ValidationResult(
                $"El método de pago debe ser uno de estos: {string.Join(", ", MetodosPago.Validos)}.",
                new[] { nameof(PaymentMethod) });
        }
    }
}
