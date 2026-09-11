namespace EntityLibrary
{
    // Un abono puntual (una fila real de PAGOS) dentro del período al que pertenece.
    public class PagoHistorialAbono
    {
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
        public DateTime FechaPago { get; set; }
    }

    // Un período pagado del historial de un jugador. Si esa cuota se pagó en más de un abono
    // (por ejemplo $20.000 + $50.000), quedan TODOS listados acá adentro — el historial es un
    // registro completo, no se resume en un solo número — pero agrupados bajo el mismo período.
    public class PagoHistorialItem
    {
        public string Periodo { get; set; } = string.Empty; // "Enero 2026", derivado de fecha_vencimiento; "-" si no está cargado
        public decimal MontoTotal { get; set; }
        public List<PagoHistorialAbono> Abonos { get; set; } = new();
    }

    public class HistorialPagosResultado
    {
        public List<PagoHistorialItem> Items { get; set; } = new();
        public int Total { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }
}
