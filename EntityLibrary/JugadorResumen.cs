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
        public DateTime? FechaAlta { get; set; }
        // null = activo. Con fecha, el jugador dejó el club y no se le emiten cuotas después de ese mes.
        public DateTime? FechaBaja { get; set; }
        public bool Activo => FechaBaja == null;

        public string NombreCompleto => $"{Apellido}, {Nombre}";
    }
}
