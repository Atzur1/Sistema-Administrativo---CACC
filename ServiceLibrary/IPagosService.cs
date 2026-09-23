using EntityLibrary;

namespace ServiceLibrary
{
    public class RegistrarPagoRequest
    {
        public int IdJugador { get; set; }
        public string Periodo { get; set; } = string.Empty; // nombre de mes: "Marzo"
        public int Anio { get; set; } // elegido en el form: año actual o alguno de los 2 anteriores
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
    }

    public class RegistrarPagoResultado
    {
        public int IdPago { get; set; }
        public int IdJugador { get; set; }
        public string Periodo { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
        public DateTime FechaPago { get; set; }
    }

    public class CobrarPagosPendientesRequest
    {
        public List<int> IdsPago { get; set; } = new();
        public string MetodoPago { get; set; } = string.Empty;
    }

    public class CobrarPagosPendientesResultado
    {
        public List<int> PagosAbonados { get; set; } = new();
        public decimal MontoTotal { get; set; }
        public DateTime FechaPago { get; set; }
    }

    public interface IPagosService
    {
        // Backea el form "Registrar pago": crea un nuevo PAGOS ya abonado para jugador+período+monto.
        // Lanza CobroInvalidoException si ese jugador ya tiene un pago abonado para ese período (re-cobro).
        RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request);

        // Cobro en lote de filas PAGOS preexistentes con estado pendiente (Estado = false).
        CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request);

        IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null);

        IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null);

        // HU-029: padrón de jugadores con su deuda; onlyDebtors deja solo a los que deben.
        IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors);

        IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador);

        IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top);

        ResumenPagos ObtenerResumen();
    }
}
