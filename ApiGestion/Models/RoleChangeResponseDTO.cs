using DaoLibrary;

namespace ApiGestion.Models;

// Canonical state of the account after a role change, read in the same transaction as the write. Undoing the change
// is another PUT with RolAnterior and this Revision.
public sealed class RoleChangeResponseDTO
{
    public int IdUsuario { get; init; }
    public int RolAnterior { get; init; }
    public int RolPortal { get; init; }
    public int Revision { get; init; }
    public string Mensaje { get; init; } = "Rol actualizado.";

    public static RoleChangeResponseDTO FromResult(CambioDeRol cambio) => new()
    {
        IdUsuario = cambio.IdUsuario,
        RolAnterior = cambio.RolAnterior,
        RolPortal = cambio.RolPortal,
        Revision = cambio.Revision,
    };
}
