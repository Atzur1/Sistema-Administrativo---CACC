using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class ChangeRoleRequestDTO
{
    [Required(ErrorMessage = "Indicá el rol."), Range(1, 2, ErrorMessage = "El rol debe ser SuperAdmin o Administrador.")]
    public int RolPortal { get; init; }
}
