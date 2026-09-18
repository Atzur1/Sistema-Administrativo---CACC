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
        public decimal SaldoPendiente { get; set; } // Ya con el beneficio de Becados y Descuentos aplicado (nunca negativo)

        // Beneficio activo de Becados y Descuentos que cubre el período de esta cuota, si tiene
        // uno — se resuelve en el momento de leer, no importa si la cuota ya estaba cargada
        // antes de asignarle el beneficio.
        public bool TieneBeneficio { get; set; }
        public string? MotivoBeneficio { get; set; }
        public string? TipoValorBeneficio { get; set; } // "%" o "$"
        public decimal? PorcentajeBeneficio { get; set; }
        public decimal? MontoFijoBeneficio { get; set; }

        public List<AbonoDetalle> Abonos { get; set; } = new();
    }
}
