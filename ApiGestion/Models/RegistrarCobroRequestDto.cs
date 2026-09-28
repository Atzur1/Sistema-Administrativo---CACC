namespace ApiGestion.Models
{
    // Cobro en lote de pagos pendientes preexistentes (PAGOS.estado = 0), por id.
    public class RegistrarCobroRequestDto
    {
        [System.ComponentModel.DataAnnotations.Required]
        [System.ComponentModel.DataAnnotations.MinLength(1)]
        [System.ComponentModel.DataAnnotations.MaxLength(100)]
        public List<int> IdsPago { get; set; } = new();
        public string MetodoPago { get; set; } = string.Empty;
    }
}
