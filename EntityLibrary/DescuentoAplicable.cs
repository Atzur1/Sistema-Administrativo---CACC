namespace EntityLibrary
{
    // Beneficio activo de Becados y Descuentos que aplica a una cuota puntual (jugador +
    // período de esa cuota). Se resuelve en el momento de leer/cobrar, nunca se guarda en PAGOS.
    public class DescuentoAplicable
    {
        public int IdJugadorDescuento { get; set; }
        public string Motivo { get; set; } = string.Empty;
        public string TipoValor { get; set; } = string.Empty; // "%" o "$"
        public decimal? Porcentaje { get; set; }
        public decimal? MontoFijo { get; set; }
    }
}
