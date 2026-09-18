namespace EntityLibrary
{
    // Fila del panel "Pendientes de cobro": deuda agrupada por jugador (PAGOS.Estado = 0).
    public class PendienteJugador
    {
        public int IdJugador { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal MontoTotal { get; set; }
        public int CantidadCuotas { get; set; }
    }

    // Fila del panel "Últimos pagos": PAGOS.Estado = 1, más recientes primero.
    public class PagoReciente
    {
        public int IdPago { get; set; }
        public int IdJugador { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string MetodoPago { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public DateTime FechaPago { get; set; }
    }

    // Métricas del header de "Cuotas y Pagos".
    public class ResumenPagos
    {
        public decimal RecaudadoAnioActual { get; set; }
        public int PagosDelMes { get; set; }
        public int CantidadPendientes { get; set; }
    }
}
