using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

// Role change of an enabled account (PUT api/usuarios/{id}/rol). Reactivar keeps using ChangeRoleRequestDTO: it has no
// revision because the account is disabled and its row is not in the enabled list.
public sealed class RoleChangeRequestDTO
{
    [Required, Range(1, 2)]
    public int RolPortal { get; init; }

    // The revision the screen saw (PortalUserResponseDTO.Revision, or the one returned by the previous change).
    // The change only applies if the account still has it.
    [Required, Range(0, int.MaxValue)]
    public int? RevisionEsperada { get; init; }
}
