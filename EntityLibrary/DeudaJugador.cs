namespace EntityLibrary
{
    // Un abono (PAGOS estado = 1) aplicado contra una cuota pendiente puntual.
    public class AbonoDetalle
    {
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
        public DateTime FechaPago { get; set; }
    }

    // Una cuota todavía pendiente (PAGOS estado = 0) de un jugador, con lo que ya abonó
    // (si pagó parcial) y lo que le sigue faltando.
    public class CuotaPendienteDetalle
    {
        public int IdPago { get; set; }
        public string Periodo { get; set; } = string.Empty; // "Septiembre 2026", derivado de fecha_vencimiento
        public decimal MontoOriginal { get; set; } // PAGOS.monto_base de la cuota: nunca se toca al abonar parcial
        public decimal SaldoPendiente { get; set; } // PAGOS.monto_final: se reduce con cada abono
        public List<AbonoDetalle> Abonos { get; set; } = new();
    }
}
