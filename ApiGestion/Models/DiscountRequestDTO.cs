namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;

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
    public const string PercentageValue = "%";
    public const string FixedAmountValue = "$";

    public const decimal MinPercentage = 0m;
    public const decimal MaxPercentage = 100m;

    [Required(ErrorMessage = "The benefit reason is required.")]
    public string Reason { get; set; } = "";

    [Required(ErrorMessage = "The value type is required.")]
    public string ValueType { get; set; } = "";

    public decimal? Percentage { get; set; }

    public decimal? FixedAmount { get; set; }

    // Mandatory since HU-012: a benefit runs for an authorised period and stops
    // on its own when that period ends, so neither end of the range can be left
    // open. Both travel as yyyy-MM-dd, the same shape the API hands back.
    [Required(ErrorMessage = "The start date is required.")]
    public string? StartDate { get; set; }

    [Required(ErrorMessage = "The end date is required.")]
    public string? EndDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        string valueType = (ValueType ?? "").Trim();

        if (valueType != PercentageValue && valueType != FixedAmountValue)
        {
            yield return new ValidationResult(
                $"The value type must be '{PercentageValue}' or '{FixedAmountValue}'.",
                new[] { nameof(ValueType) });
            yield break;
        }

        if (valueType == PercentageValue)
        {
            if (Percentage == null)
            {
                yield return new ValidationResult(
                    "A percentage benefit requires a percentage.",
                    new[] { nameof(Percentage) });
            }
            else if (Percentage <= MinPercentage || Percentage > MaxPercentage)
            {
                yield return new ValidationResult(
                    $"The percentage must be greater than {MinPercentage} and at most {MaxPercentage}.",
                    new[] { nameof(Percentage) });
            }

            if (FixedAmount != null)
            {
                yield return new ValidationResult(
                    "A percentage benefit cannot carry a fixed amount.",
                    new[] { nameof(FixedAmount) });
            }
        }
        else
        {
            if (FixedAmount == null)
            {
                yield return new ValidationResult(
                    "A fixed amount benefit requires an amount.",
                    new[] { nameof(FixedAmount) });
            }
            else if (FixedAmount <= 0)
            {
                yield return new ValidationResult(
                    "The fixed amount must be greater than zero.",
                    new[] { nameof(FixedAmount) });
            }

            if (Percentage != null)
            {
                yield return new ValidationResult(
                    "A fixed amount benefit cannot carry a percentage.",
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
                "The start date must be a valid date in yyyy-MM-dd format.",
                new[] { nameof(StartDate) });
        }

        if (!string.IsNullOrWhiteSpace(EndDate) && end == null)
        {
            yield return new ValidationResult(
                "The end date must be a valid date in yyyy-MM-dd format.",
                new[] { nameof(EndDate) });
        }

        // Strictly later, not "later or equal": a range that starts and ends the
        // same day describes a one-day benefit, which the club does not grant.
        if (start != null && end != null && end <= start)
        {
            yield return new ValidationResult(
                "The end date must be later than the start date.",
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
