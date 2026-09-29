namespace EntityLibrary
{
    // Persona cargada por el otro equipo con FK_id_rol de SuperAdmin (1) o
    // Administrador (2) que todavía no tiene acceso al Portal Administrativo
    // (acceso_portal = 0). FkIdRol es solo una sugerencia para precargar el
    // desplegable de rol al habilitar: el rol real que rige el acceso es
    // rol_portal, decidido por el SuperAdmin en ese momento, no copiado de acá.
    public class UsuarioCandidato
    {
        public int IdUsuario { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int FkIdRolSugerido { get; set; }
    }
}
