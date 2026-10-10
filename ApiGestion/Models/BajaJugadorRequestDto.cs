namespace ApiGestion.Models
{
    public class BajaJugadorRequestDto
    {
        // Día en que el jugador dejó el club. Sin valor, se toma la fecha de hoy.
        public DateTime? Fecha { get; set; }
    }
}
