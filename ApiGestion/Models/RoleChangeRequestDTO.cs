using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

// Role change of an enabled account (PUT api/usuarios/{id}/rol). Reactivar keeps using ChangeRoleRequestDTO: it has no
// revision because the account is disabled and its row is not in the enabled list.
public sealed class RoleChangeRequestDTO
{
    [Required(ErrorMessage = "Indicá el rol."), Range(1, 2, ErrorMessage = "El rol debe ser SuperAdmin o Administrador.")]
    public int RolPortal { get; init; }

    // The revision the screen saw (PortalUserResponseDTO.Revision, or the one returned by the previous change).
    // The change only applies if the account still has it.
    [Required(ErrorMessage = "Indicá la revisión esperada."), Range(0, int.MaxValue, ErrorMessage = "La revisión esperada no es válida.")]
    public int? RevisionEsperada { get; init; }
}
