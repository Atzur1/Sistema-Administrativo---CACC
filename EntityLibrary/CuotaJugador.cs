namespace EntityLibrary
{
    // HU-025: una fila de PAGOS de concepto 'Cuota' tal como sale de la base, con el saldo ya
    // ajustado por el beneficio activo (mismo criterio que el detalle de deuda). La capa de
    // negocio agrupa estas filas por período para armar el estado de cada cuota.
    public class CuotaMovimiento
    {
        public int IdPago { get; set; }
        public bool Estado { get; set; } // false = cuota pendiente, true = abono/cobro registrado
        public decimal MontoBase { get; set; }
        public decimal MontoFinal { get; set; }
        public decimal SaldoAjustado { get; set; } // Solo tiene sentido en la fila pendiente
        public DateTime FechaVencimiento { get; set; }
        public DateTime? FechaPago { get; set; }
        public string? MetodoPago { get; set; }
        public string? MotivoBeneficio { get; set; }
    }

    // HU-025: una cuota mensual del jugador en la tabla de la ficha financiera. Estado es
    // "Pendiente", "Vencido" o "Pagado"; solo las pendientes/vencidas con saldo son cobrables.
    public class CuotaJugador
    {
        public int IdPago { get; set; } // Pendiente/Vencido: la fila que se cobra. Pagado: el último abono.
        public string Periodo { get; set; } = string.Empty; // "Marzo 2026"
        public DateTime FechaVencimiento { get; set; }
        public decimal MontoCuota { get; set; } // monto_base: importe histórico con que se emitió
        public decimal SaldoPendiente { get; set; } // Lo que se cobraría hoy (0 si está pagada o cubierta)
        public decimal MontoAbonado { get; set; } // Suma de lo ya cobrado para ese período
        public string Estado { get; set; } = string.Empty;
        public bool CubiertaPorBeneficio { get; set; }
        public string? MotivoBeneficio { get; set; }
        public string? MetodoPago { get; set; } // Del último cobro del período
        public DateTime? FechaPago { get; set; } // Del último cobro del período
    }

    public static class EstadosCuota
    {
        public const string Pendiente = "Pendiente";
        public const string Vencido = "Vencido";
        public const string Pagado = "Pagado";
    }
}
