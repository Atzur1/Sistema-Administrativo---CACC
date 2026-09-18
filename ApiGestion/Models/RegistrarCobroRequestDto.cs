namespace ApiGestion.Models
{
    // Cobro en lote de pagos pendientes preexistentes (PAGOS.estado = 0), por id.
    public class RegistrarCobroRequestDto
    {
        public List<int> IdsPago { get; set; } = new();
        public string MetodoPago { get; set; } = string.Empty;
    }
}
