namespace EntityLibrary
{
    // Una fila del historial de pagos de un jugador.
    public class PagoHistorialItem
    {
        public int IdPago { get; set; }
        public DateTime FechaPago { get; set; }
        public string Periodo { get; set; } = string.Empty; // "Enero 2026", derivado de fecha_vencimiento; "-" si no está cargado
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
    }

    public class HistorialPagosResultado
    {
        public List<PagoHistorialItem> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
