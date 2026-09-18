namespace EntityLibrary
{
    // Proyección liviana de JUGADORES + PERSONA + CATEGORIAS para listados y buscadores.
    public class JugadorResumen
    {
        public int IdJugador { get; set; }
        public string Nombre { get; set; } = string.Empty;
        public string Apellido { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public string Genero { get; set; } = string.Empty; // PERSONA.genero: "Masculino" | "Femenino"

        public string NombreCompleto => $"{Apellido}, {Nombre}";
    }
}
