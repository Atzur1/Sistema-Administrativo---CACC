namespace EntityLibrary
{
    // Fila de la tabla "Usuarios y Permisos": alguien con acceso_portal = 1 hoy,
    // sea SuperAdmin o Administrador.
    public class UsuarioPortal
    {
        public int IdUsuario { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int RolPortal { get; set; }
        public bool Activo { get; set; }
        public bool ActivacionPendiente { get; set; }

        // token_version de la cuenta (solo en ListarHabilitados). Sube con cada cambio de rol, deshabilitación,
        // cierre de sesión o contraseña completada, y nunca baja: un cambio de rol se condiciona a esta revisión.
        public int Revision { get; set; }
    }
}
