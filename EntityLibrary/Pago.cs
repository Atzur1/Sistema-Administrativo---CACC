namespace EntityLibrary
{
    // Refleja la tabla PAGOS real: no hay una tabla "Cuotas" separada, cada fila de PAGOS
    // ES la cuota (pendiente mientras Estado = false, abonada cuando Estado = true).
    public class Pago
    {
        public int IdPago { get; set; }
        public int IdJugador { get; set; }
        public decimal MontoBase { get; set; }
        public int? IdJugadorDescuento { get; set; }
        public decimal MontoFinal { get; set; }
        public DateTime? FechaPago { get; set; }
        public string? MetodoPago { get; set; }
        public DateTime? FechaVencimiento { get; set; }
        public bool Estado { get; set; }
    }

    public static class MetodosPago
    {
        public static readonly string[] Validos = { "Transferencia", "Efectivo" };

        public static bool EsValido(string metodo) =>
            Validos.Any(m => string.Equals(m, metodo, StringComparison.OrdinalIgnoreCase));
    }
}
