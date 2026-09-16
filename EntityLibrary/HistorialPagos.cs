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
        public decimal MontoTotal { get; set; } // Lo que efectivamente se cobró (suma de los abonos)
        public decimal MontoOriginal { get; set; } // El valor completo de la cuota, antes del beneficio (si tiene uno)

        // Beneficio de Becados y Descuentos que se le aplicó a esta cuota al pagarla, si tuvo uno
        // — así se ve por qué MontoTotal es menor que MontoOriginal.
        public bool TieneBeneficio { get; set; }
        public string? MotivoBeneficio { get; set; }
        public string? TipoValorBeneficio { get; set; } // "%" o "$"
        public decimal? PorcentajeBeneficio { get; set; }
        public decimal? MontoFijoBeneficio { get; set; }

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
