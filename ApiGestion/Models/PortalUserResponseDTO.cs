using EntityLibrary;

namespace ApiGestion.Models;

// Row of the "Usuarios y Permisos" table: an account with portal access, enabled or revoked.
public class PortalUserResponseDTO
{
    public int IdUsuario { get; set; }
    public string Nombre { get; set; } = "";
    public string Apellido { get; set; } = "";
    public string Dni { get; set; } = "";
    public string Email { get; set; } = "";
    public int RolPortal { get; set; }
    public bool Activo { get; set; }
    public bool ActivacionPendiente { get; set; }

    public static PortalUserResponseDTO FromEntity(UsuarioPortal user)
    {
        return new PortalUserResponseDTO
        {
            IdUsuario = user.IdUsuario,
            Nombre = user.Nombre,
            Apellido = user.Apellido,
            Dni = user.Dni,
            Email = user.Email,
            RolPortal = user.RolPortal,
            Activo = user.Activo,
            ActivacionPendiente = user.ActivacionPendiente
        };
    }
}
