using EntityLibrary;

namespace DaoLibrary
{
    public enum HabilitarResultado
    {
        Ok,
        NoEncontrado,
        EmailNoCoincide,
        RolInvalido
    }

    public interface IUsuariosPortalDao
    {
        // Gente cargada por el otro equipo con FK_id_rol de SuperAdmin/Administrador
        // que todavía no tiene acceso al portal.
        IReadOnlyList<UsuarioCandidato> ListarCandidatos();

        // Gente con acceso_portal = 1 hoy.
        IReadOnlyList<UsuarioPortal> ListarHabilitados();

        // Corrige los datos de un candidato todavía sin acceso, por si el otro equipo
        // los cargó mal — sobre todo el mail: evita que el enlace de activación
        // termine en manos de otra persona. Solo antes de habilitar: una vez con
        // acceso, editar estos datos es un cambio de identidad de cuenta distinto,
        // no una corrección de carga.
        bool EditarDatosCandidato(int idUsuario, string nombre, string apellido, string email);

        // Confirma el mail (evita habilitar a la persona equivocada por un error de
        // carga del otro equipo) y otorga acceso_portal=1 + rol_portal. Deja
        // activacion_pendiente=1: falta que la persona complete su contraseña con
        // el enlace de activación. Devuelve el email para poder enviar el correo.
        (HabilitarResultado Resultado, string? Email) Habilitar(int idUsuario, string emailConfirmado, int rolPortal);

        // Cambia el rol de alguien ya habilitado. Corta su sesión abierta
        // (token_version+1). Rechaza dejar el club sin ningún SuperAdmin habilitado.
        bool CambiarRol(int idUsuario, int nuevoRolPortal);

        // Revoca el acceso y corta la sesión abierta. Mismo resguardo que CambiarRol:
        // no puede deshabilitar al último SuperAdmin habilitado.
        bool Deshabilitar(int idUsuario);
    }
}
