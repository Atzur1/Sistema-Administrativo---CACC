namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;
using EntityLibrary;

// Inbound contract of the player registration form (HU-033)
public class PlayerRequestDTO : IValidatableObject
{
    public const string MaleGender = "Masculino";
    public const string FemaleGender = "Femenino";

    [Required(ErrorMessage = "The first name is required.")]
    [StringLength(100, ErrorMessage = "The first name cannot be longer than 100 characters.")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "The last name is required.")]
    [StringLength(100, ErrorMessage = "The last name cannot be longer than 100 characters.")]
    public string LastName { get; set; } = "";

    [Required(ErrorMessage = "The DNI is required.")]
    [RegularExpression(@"^\s*\d{7,8}\s*$", ErrorMessage = "The DNI must have 7 or 8 digits, without dots.")]
    public string Dni { get; set; } = "";

    [Required(ErrorMessage = "The birth date is required.")]
    public DateTime? BirthDate { get; set; }

    [Required(ErrorMessage = "The gender is required.")]
    public string Gender { get; set; } = "";

    [Required(ErrorMessage = "The category is required.")]
    [Range(1, long.MaxValue, ErrorMessage = "The category is required.")]
    public long? CategoryId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Gender) && Gender != MaleGender && Gender != FemaleGender)
        {
            yield return new ValidationResult(
                $"The gender must be '{MaleGender}' or '{FemaleGender}'.",
                new[] { nameof(Gender) });
        }

        if (BirthDate != null && BirthDate.Value.Date >= RelojNegocio.Hoy)
        {
            yield return new ValidationResult(
                "The birth date must be in the past.",
                new[] { nameof(BirthDate) });
        }
    }
}
