using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class ChangeRoleRequestDTO
{
    [Required, Range(1, 2)]
    public int RolPortal { get; init; }
}
