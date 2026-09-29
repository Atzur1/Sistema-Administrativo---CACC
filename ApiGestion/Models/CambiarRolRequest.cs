using System.ComponentModel.DataAnnotations;

namespace ApiGestion.Models;

public sealed class CambiarRolRequest
{
    [Required, Range(1, 2)]
    public int RolPortal { get; init; }
}
