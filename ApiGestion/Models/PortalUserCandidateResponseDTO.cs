using EntityLibrary;

namespace ApiGestion.Models;

// Person with a SuperAdmin/Administrador role who does not have portal access yet.
public class PortalUserCandidateResponseDTO
{
    public int IdUsuario { get; set; }
    public string Nombre { get; set; } = "";
    public string Apellido { get; set; } = "";
    public string Dni { get; set; } = "";
    public string Email { get; set; } = "";
    public int FkIdRolSugerido { get; set; }

    public static PortalUserCandidateResponseDTO FromEntity(UsuarioCandidato candidate)
    {
        return new PortalUserCandidateResponseDTO
        {
            IdUsuario = candidate.IdUsuario,
            Nombre = candidate.Nombre,
            Apellido = candidate.Apellido,
            Dni = candidate.Dni,
            Email = candidate.Email,
            FkIdRolSugerido = candidate.FkIdRolSugerido
        };
    }
}
