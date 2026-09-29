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
    }
}
