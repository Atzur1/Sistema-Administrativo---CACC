using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

// El mail no forma parte de la corrección: no se modifica desde el portal (ver IUsuariosPortalDao).
public sealed class EditCandidateRequestDTO
{
    [Required(ErrorMessage = "Indicá el nombre."), MaxLength(100, ErrorMessage = "El nombre no puede superar los 100 caracteres.")]
    public string Nombre { get; init; } = string.Empty;

    [Required(ErrorMessage = "Indicá el apellido."), MaxLength(100, ErrorMessage = "El apellido no puede superar los 100 caracteres.")]
    public string Apellido { get; init; } = string.Empty;

    // Con o sin puntos ("30.111.222"): el servidor lo guarda solo con dígitos.
    [Required(ErrorMessage = "Indicá el DNI."), MaxLength(40, ErrorMessage = "El DNI no puede superar los 40 caracteres.")]
    public string Dni { get; init; } = string.Empty;
}
