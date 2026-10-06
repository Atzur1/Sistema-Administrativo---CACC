using EntityLibrary;

namespace DaoLibrary
{
    public enum HabilitarResultado
    {
        Ok,
        NoEncontrado,
        EmailNoCoincide,
        RolInvalido,
        // El DNI es el usuario del login: sin un DNI válido, o con uno que ya usa otra cuenta con acceso,
        // no se puede habilitar.
        DniInvalido,
        DniEnUso
    }

    public enum EditarCandidatoResultado
    {
        Ok,
        NoEncontrado,
        DniInvalido,
        DniEnUso
    }

    public interface IUsuariosPortalDao
    {
        // Gente cargada por el otro equipo con FK_id_rol de SuperAdmin/Administrador
        // que todavía no tiene acceso al portal ni tuvo nunca (sin password_hash).
        IReadOnlyList<UsuarioCandidato> ListarCandidatos();

        // Gente con acceso_portal = 1 hoy.
        IReadOnlyList<UsuarioPortal> ListarHabilitados();

        // Tuvieron acceso alguna vez y se les deshabilitó (acceso_portal = 0, pero ya
        // tienen password_hash y rol_portal de cuando estaban activos).
        IReadOnlyList<UsuarioPortal> ListarDeshabilitados();

        // Corrige los datos de un candidato todavía sin acceso, por si el otro equipo
        // los cargó mal — el mail (evita que el enlace de activación termine en manos de
        // otra persona) y el DNI (es el usuario con el que va a ingresar). Solo antes de
        // habilitar: una vez con acceso, editar estos datos es un cambio de identidad de
        // cuenta distinto, no una corrección de carga.
        EditarCandidatoResultado EditarDatosCandidato(int idUsuario, string nombre, string apellido, string dni, string email);

        // Confirma el mail (evita habilitar a la persona equivocada por un error de
        // carga del otro equipo), exige un DNI válido y no repetido entre las cuentas con
        // acceso (lo guarda solo con dígitos) y otorga acceso_portal=1 + rol_portal. Deja
        // activacion_pendiente=1: falta que la persona complete su contraseña con
        // el enlace de activación. Devuelve el email para poder enviar el correo.
        (HabilitarResultado Resultado, string? Email) Habilitar(int idUsuario, string emailConfirmado, int rolPortal);

        // Cambia el rol de alguien ya habilitado. Corta su sesión abierta
        // (token_version+1). Rechaza dejar el club sin ningún SuperAdmin habilitado.
        bool CambiarRol(int idUsuario, int nuevoRolPortal);

        // Revoca el acceso y corta la sesión abierta. Mismo resguardo que CambiarRol:
        // no puede deshabilitar al último SuperAdmin habilitado.
        bool Deshabilitar(int idUsuario);

        // Le devuelve acceso_portal a alguien de ListarDeshabilitados, con el mismo
        // rol_portal y la misma contraseña que ya tenía — sin token ni mail nuevo.
        bool Reactivar(int idUsuario);
    }
}
