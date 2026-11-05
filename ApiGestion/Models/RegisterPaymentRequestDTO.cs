namespace ApiGestion.Models
{
    // Backea el form "Registrar pago" del frontend (jugador + período + monto + método).
    public class RegisterPaymentRequestDTO
    {
        public int IdJugador { get; set; }
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indicá el período.")]
        public string Periodo { get; set; } = string.Empty;
        public int Anio { get; set; }
        public decimal Monto { get; set; }
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indicá el método de pago.")]
        public string MetodoPago { get; set; } = string.Empty;
    }
}
