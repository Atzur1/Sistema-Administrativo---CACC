namespace ApiGestion.Models
{
    // HU-025: cobro de una o varias cuotas pendientes/vencidas de un jugador. El operador NO
    // viaja en el body: el controller lo toma del claim idUsuario del JWT.
    public class RegistrarCobroRequestDto
    {
        public int IdJugador { get; set; }

        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        [System.ComponentModel.DataAnnotations.MaxLength(100)]
        public List<int> IdsPago { get; set; } = new();
        public string MetodoPago { get; set; } = string.Empty;
    }
}
