namespace ApiGestion.Models
{
    // HU-025: cobro de una o varias cuotas pendientes/vencidas de un jugador. El operador NO
    // viaja en el body: el controller lo toma del claim idUsuario del JWT.
    public class CollectFeesRequestDTO
    {
        public int IdJugador { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Elegí al menos una cuota para cobrar.")]
        [System.ComponentModel.DataAnnotations.MinLength(1, ErrorMessage = "Elegí al menos una cuota para cobrar.")]
        [System.ComponentModel.DataAnnotations.MaxLength(100, ErrorMessage = "Se pueden cobrar hasta 100 cuotas por vez.")]
        public List<int> IdsPago { get; set; } = new();
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Indicá el método de pago.")]
        public string MetodoPago { get; set; } = string.Empty;
    }
}
