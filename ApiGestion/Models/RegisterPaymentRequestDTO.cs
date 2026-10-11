namespace ApiGestion.Models
{
    // Backea el form "Registrar pago" del frontend (jugador + período + monto + método).
    public class RegisterPaymentRequestDTO
    {
        public int IdJugador { get; set; }
        public string Periodo { get; set; } = string.Empty;
        public int Anio { get; set; }
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
    }
}
