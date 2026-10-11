namespace ApiGestion.Models
{
    public class PlayerLeaveRequestDTO
    {
        // Día en que el jugador dejó el club. Sin valor, se toma la fecha de hoy.
        public DateTime? Fecha { get; set; }
    }
}
