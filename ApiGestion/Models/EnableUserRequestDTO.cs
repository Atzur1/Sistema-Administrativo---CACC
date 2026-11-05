using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class EnableUserRequestDTO
{
    // El SuperAdmin confirma el mail que ve en pantalla antes de habilitar: si el
    // otro equipo cargó mal el correo, el enlace de activación le llegaría a un
    // desconocido, y eso es acceso de administrador.
    [Required(ErrorMessage = "Indicá el correo electrónico."), EmailAddress(ErrorMessage = "El correo electrónico no es válido."), MaxLength(254, ErrorMessage = "El correo electrónico no puede superar los 254 caracteres.")]
    public string Email { get; init; } = string.Empty;

    [Required(ErrorMessage = "Indicá el rol."), Range(1, 2, ErrorMessage = "El rol debe ser SuperAdmin o Administrador.")]
    public int RolPortal { get; init; }
}
