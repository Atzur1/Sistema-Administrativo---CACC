namespace EntityLibrary
{
    // Un mes que quedó con cuotas sin generar (por ejemplo, porque la API estuvo apagada el día 10): cuántos
    // jugadores estaban en el club, tenían un arancel vigente y no tienen la cuota, y por cuánto en total.
    public class MesConCuotasFaltantes
    {
        public DateTime Periodo { get; set; }
        public int Cantidad { get; set; }
        public decimal MontoTotal { get; set; }
    }

    // Una cuota que se emitiría al generar ese mes: a quién y por qué monto (su arancel de ese mes).
    public class CuotaFaltante
    {
        public int IdJugador { get; set; }
        public string NombreCompleto { get; set; } = string.Empty;
        public string Dni { get; set; } = string.Empty;
        public string Categoria { get; set; } = string.Empty;
        public decimal Monto { get; set; }
    }
}
