namespace ApiGestion.Models
{
    // Backea el form "Registrar pago" del frontend (jugador + período + monto + método).
    public class RegistrarPagoRequestDto
    {
        public int IdJugador { get; set; }
        public string Periodo { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
    }
}
