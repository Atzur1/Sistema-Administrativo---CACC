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
        // Operador autenticado (claim idUsuario del JWT); lo completa el controller, nunca el body.
        public int? IdUsuarioRegistro { get; set; }
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

    // HU-025: cobro de una o varias cuotas pendientes/vencidas de un mismo jugador.
    public class CobrarPagosPendientesRequest
    {
        public int IdJugador { get; set; }
        public List<int> IdsPago { get; set; } = new();
        public string MetodoPago { get; set; } = string.Empty;
        // Operador autenticado (claim idUsuario del JWT); lo completa el controller, nunca el body.
        public int IdUsuarioRegistro { get; set; }
    }

    public class CuotaCobrada
    {
        public int IdPago { get; set; }
        public string Periodo { get; set; } = string.Empty;
        public decimal Monto { get; set; }
        public string Estado { get; set; } = EstadosCuota.Pagado;
    }

    public class CobrarPagosPendientesResultado
    {
        public int IdJugador { get; set; }
        public List<int> PagosAbonados { get; set; } = new();
        public List<CuotaCobrada> Cuotas { get; set; } = new();
        public decimal MontoTotal { get; set; }
        public string MetodoPago { get; set; } = string.Empty;
        public DateTime FechaPago { get; set; }
        public DateTime FechaHoraRegistro { get; set; }
    }

    public interface IPagosService
    {
        // Backea el form "Registrar pago": crea un nuevo PAGOS ya abonado para jugador+período+monto.
        // Lanza CobroInvalidoException si ese jugador ya tiene un pago abonado para ese período (re-cobro).
        RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request);

        // HU-025: cobro atómico de cuotas pendientes/vencidas de un jugador (todas o ninguna).
        CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request);

        // HU-025: cuotas del jugador con su estado (Pendiente, Vencido o Pagado) para la ficha financiera.
        IReadOnlyList<CuotaJugador> ObtenerCuotasJugador(int idJugador);

        // HU-024: estado de cuenta del jugador (cuotas con estado y total a abonar); null si no existe.
        PlayerStatement? GetPlayerStatement(int playerId);

        IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null);

        IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null);

        // HU-029: padrón de jugadores con su deuda; onlyDebtors deja solo a los que deben.
        IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors);

        IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador);

        IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top);

        IReadOnlyList<PagoReciente> ObtenerTodosLosPagos();

        ResumenPagos ObtenerResumen();

        ResumenPagosHoy ObtenerResumenHoy();
    }
}
