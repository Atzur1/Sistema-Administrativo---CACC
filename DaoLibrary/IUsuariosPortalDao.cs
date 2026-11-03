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

    public enum CambiarRolResultado
    {
        Ok,
        // No existe o ya no tiene acceso al portal.
        NoEncontrado,
        // La cuenta cambió (rol, sesión, contraseña o acceso) después de la revisión que se mandó.
        RevisionDesactualizada,
        // No quedaría ningún otro SuperAdmin que pueda ingresar.
        UltimoSuperAdmin,
        RolInvalido
    }

    // Estado de la cuenta después de cambiarle el rol, leído en la misma transacción. Con Revision se condiciona el
    // próximo cambio (por ejemplo, deshacer este) a que nada la haya modificado en el medio.
    public sealed record CambioDeRol(int IdUsuario, int RolAnterior, int RolPortal, int Revision);

    public interface IUsuariosPortalDao
    {
        // Gente cargada por el otro equipo con FK_id_rol de SuperAdmin/Administrador
        // que todavía no tiene acceso al portal ni tuvo nunca (sin contrasenia).
        IReadOnlyList<UsuarioCandidato> ListarCandidatos();

        // Gente con acceso_portal = 1 hoy.
        IReadOnlyList<UsuarioPortal> ListarHabilitados();

        // Tuvieron acceso alguna vez y se les deshabilitó (acceso_portal = 0, pero ya
        // tienen contrasenia y rol_portal de cuando estaban activos).
        IReadOnlyList<UsuarioPortal> ListarDeshabilitados();

        // Corrige nombre, apellido y DNI de un candidato todavía sin acceso, por si el otro equipo los cargó
        // mal (el DNI es el usuario con el que va a ingresar). El MAIL NO se modifica desde el portal: el enlace
        // de activación va a ese correo, y si el SuperAdmin pudiera cambiarlo podría habilitar la cuenta de otra
        // persona con su propio correo y actuar a su nombre. Un mail mal cargado lo corrige el otro equipo.
        EditarCandidatoResultado EditarDatosCandidato(int idUsuario, string nombre, string apellido, string dni);

        // Confirma el mail (evita habilitar a la persona equivocada por un error de
        // carga del otro equipo), exige un DNI válido y no repetido entre las cuentas con
        // acceso (lo guarda solo con dígitos) y otorga acceso_portal=1 + rol_portal. Deja
        // activacion_pendiente=1: falta que la persona complete su contraseña con
        // el enlace de activación. Devuelve el email para poder enviar el correo.
        (HabilitarResultado Resultado, string? Email) Habilitar(int idUsuario, string emailConfirmado, int rolPortal);

        // Cambia el rol de alguien ya habilitado, solo si su token_version sigue siendo revisionEsperada (la que vio
        // el SuperAdmin): así un pedido viejo no pisa un cambio posterior, aunque el rol haya vuelto al mismo valor.
        // Corta su sesión abierta (token_version+1, que es la revisión nueva). Rechaza dejar el club sin otro
        // SuperAdmin que pueda ingresar (con acceso, activo y con la activación completa).
        (CambiarRolResultado Resultado, CambioDeRol? Cambio) CambiarRol(int idUsuario, int nuevoRolPortal, int revisionEsperada);

        // Revoca el acceso y corta la sesión abierta. Mismo resguardo que CambiarRol:
        // no puede deshabilitar al último SuperAdmin habilitado.
        bool Deshabilitar(int idUsuario);

        // Le devuelve acceso_portal a alguien de ListarDeshabilitados, con el rol que elige el SuperAdmin (en
        // pantalla viene elegido el que tenía) y la misma contraseña que ya tenía — sin token ni mail nuevo.
        bool Reactivar(int idUsuario, int rolPortal);
    }
}
