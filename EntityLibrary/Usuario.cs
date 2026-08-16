namespace EntityLibrary
{
    public class Usuario
    {
        public int IdUsuario { get; set; }
        public string Email { get; set; } = string.Empty;
        public string Contrasenia { get; set; } = string.Empty;
        public int IdRol { get; set; }
    }
}