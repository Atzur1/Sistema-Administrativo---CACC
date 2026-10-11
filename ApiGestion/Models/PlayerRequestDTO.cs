namespace ApiGestion.Models;

using System.ComponentModel.DataAnnotations;
using EntityLibrary;

// Inbound contract of the player registration form (HU-033)
public class PlayerRequestDTO : IValidatableObject
{
    public const string MaleGender = Generos.Masculino;
    public const string FemaleGender = Generos.Femenino;

    [Required(ErrorMessage = "Indicá el nombre.")]
    [StringLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres.")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "Indicá el apellido.")]
    [StringLength(100, ErrorMessage = "El apellido no puede superar los 100 caracteres.")]
    public string LastName { get; set; } = "";

    [Required(ErrorMessage = "Indicá el DNI.")]
    [RegularExpression(@"^\s*\d{7,8}\s*$", ErrorMessage = "El DNI debe tener 7 u 8 dígitos, sin puntos.")]
    public string Dni { get; set; } = "";

    [Required(ErrorMessage = "Indicá la fecha de nacimiento.")]
    public DateTime? BirthDate { get; set; }

    [Required(ErrorMessage = "Indicá el género.")]
    public string Gender { get; set; } = "";

    [Required(ErrorMessage = "Indicá la categoría.")]
    [Range(1, long.MaxValue, ErrorMessage = "Indicá la categoría.")]
    public long? CategoryId { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Gender) && Gender != MaleGender && Gender != FemaleGender)
        {
            yield return new ValidationResult(
                $"El género debe ser '{MaleGender}' o '{FemaleGender}'.",
                new[] { nameof(Gender) });
        }

        if (BirthDate != null && BirthDate.Value.Date >= RelojNegocio.Hoy)
        {
            yield return new ValidationResult(
                "La fecha de nacimiento debe ser anterior a hoy.",
                new[] { nameof(BirthDate) });
        }
    }
}
