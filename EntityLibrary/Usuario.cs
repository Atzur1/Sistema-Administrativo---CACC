namespace EntityLibrary
{
    public class Usuario
    {
        public int IdUsuario { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public int IdRol { get; set; }
        public int TokenVersion { get; set; }
        public bool AccesoPortal { get; set; }
        public int? RolPortal { get; set; }
        // Portal Deportivo: rol de ROLES (FK_id_rol) y categorias del staff. Van en el token para que el Deportivo autorice.
        public int? IdRolDeportivo { get; set; }
        public string? NombreRolDeportivo { get; set; }
        public List<int> IdsCategorias { get; set; } = new();
    }
}
