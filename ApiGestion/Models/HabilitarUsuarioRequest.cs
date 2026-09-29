using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class HabilitarUsuarioRequest
{
    // El SuperAdmin confirma el mail que ve en pantalla antes de habilitar: si el
    // otro equipo cargó mal el correo, el enlace de activación le llegaría a un
    // desconocido, y eso es acceso de administrador.
    [Required, EmailAddress, MaxLength(254)]
    public string Email { get; init; } = string.Empty;

    [Required, Range(1, 2)]
    public int RolPortal { get; init; }
}
