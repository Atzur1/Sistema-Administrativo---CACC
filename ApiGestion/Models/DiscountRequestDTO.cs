namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;
using EntityLibrary;

// Inbound contract to assign or edit a player's benefit (HU-011).
//
// The player is not part of the body: it travels in the route, so a request can
// never claim to be for one player while pointing at another.
//
// Percentage and FixedAmount are mutually exclusive and ValueType decides which
// one is read. Validate() below rejects any request that fills the wrong one,
// and the table constraint CK_JUGDESC_VALOR_EXCLUYENTE holds the same rule in
// the engine for anything that bypasses the API.
public class DiscountRequestDTO : IValidatableObject
{
    public const string PercentageValue = Discount.PercentageValueType;
    public const string FixedAmountValue = Discount.FixedAmountValueType;

    public const decimal MinPercentage = 0m;
    public const decimal MaxPercentage = 100m;

    [Required(ErrorMessage = "Indicá el motivo de la bonificación.")]
    public string Reason { get; set; } = "";

    [Required(ErrorMessage = "Indicá si la bonificación es un porcentaje o un monto fijo.")]
    public string ValueType { get; set; } = "";

    public decimal? Percentage { get; set; }

    public decimal? FixedAmount { get; set; }

    // Mandatory since HU-012: a benefit runs for an authorised period and stops
    // on its own when that period ends, so neither end of the range can be left
    // open. Both travel as yyyy-MM-dd, the same shape the API hands back.
    [Required(ErrorMessage = "Indicá la fecha de inicio.")]
    public string? StartDate { get; set; }

    [Required(ErrorMessage = "Indicá la fecha de fin.")]
    public string? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        string valueType = (ValueType ?? "").Trim();

        if (valueType != PercentageValue && valueType != FixedAmountValue)
        {
            yield return new ValidationResult(
                $"El tipo de valor debe ser '{PercentageValue}' o '{FixedAmountValue}'.",
                new[] { nameof(ValueType) });
            yield break;
        }

        if (valueType == PercentageValue)
        {
            if (Percentage == null)
            {
                yield return new ValidationResult(
                    "Indicá el porcentaje de la bonificación.",
                    new[] { nameof(Percentage) });
            }
            else if (Percentage <= MinPercentage || Percentage > MaxPercentage)
            {
                yield return new ValidationResult(
                    $"El porcentaje debe ser mayor a {MinPercentage} y como máximo {MaxPercentage}.",
                    new[] { nameof(Percentage) });
            }

            if (FixedAmount != null)
            {
                yield return new ValidationResult(
                    "Una bonificación por porcentaje no puede tener además un monto fijo.",
                    new[] { nameof(FixedAmount) });
            }
        }
        else
        {
            if (FixedAmount == null)
            {
                yield return new ValidationResult(
                    "Indicá el monto fijo de la bonificación.",
                    new[] { nameof(FixedAmount) });
            }
            else if (FixedAmount <= 0)
            {
                yield return new ValidationResult(
                    "El monto fijo debe ser mayor a cero.",
                    new[] { nameof(FixedAmount) });
            }

            if (Percentage != null)
            {
                yield return new ValidationResult(
                    "Una bonificación por monto fijo no puede tener además un porcentaje.",
                    new[] { nameof(Percentage) });
            }
        }

        // Dates arrive as yyyy-MM-dd, the same shape the API hands back
        DateTime? start = ParseDate(StartDate);
        DateTime? end = ParseDate(EndDate);

        // [Required] already rejects a missing date, so a blank value reports
        // once and not twice. What is checked here is the shape of what did
        // arrive.
        if (!string.IsNullOrWhiteSpace(StartDate) && start == null)
        {
            yield return new ValidationResult(
                "La fecha de inicio no es válida (formato aaaa-mm-dd).",
                new[] { nameof(StartDate) });
        }

        if (!string.IsNullOrWhiteSpace(EndDate) && end == null)
        {
            yield return new ValidationResult(
                "La fecha de fin no es válida (formato aaaa-mm-dd).",
                new[] { nameof(EndDate) });
        }

        // Strictly later, not "later or equal": a range that starts and ends the
        // same day describes a one-day benefit, which the club does not grant.
        if (start != null && end != null && end <= start)
        {
            yield return new ValidationResult(
                "La fecha de fin debe ser posterior a la de inicio.",
                new[] { nameof(EndDate) });
        }
    }

    // Empty string counts as "not sent": an untouched date input posts "" and
    // that should mean no date, not an invalid one.
    public static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTime.TryParseExact(
            value.Trim(),
            "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out DateTime parsed)
            ? parsed
            : null;
    }
}
