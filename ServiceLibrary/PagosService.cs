using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;

namespace ServiceLibrary
{
    public class PagosService : IPagosService
    {
        private static readonly Dictionary<string, int> MesesPorNombre = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Enero"] = 1, ["Febrero"] = 2, ["Marzo"] = 3, ["Abril"] = 4, ["Mayo"] = 5, ["Junio"] = 6,
            ["Julio"] = 7, ["Agosto"] = 8, ["Septiembre"] = 9, ["Octubre"] = 10, ["Noviembre"] = 11, ["Diciembre"] = 12
        };

        private readonly IPagosDao _pagosDao;
        private readonly ISqlTransactionRunner _transactionRunner;

        public PagosService(IPagosDao pagosDao, ISqlTransactionRunner transactionRunner)
        {
            _pagosDao = pagosDao;
            _transactionRunner = transactionRunner;
        }

        public RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request)
        {
            int mes = ValidarSolicitudDeRegistro(request);
            int anio = DateTime.Now.Year;

            // Todo corre dentro de una única transacción: la validación de re-cobro (con lock) y el
            // insert son atómicos, así que ningún request concurrente puede colarse entre medio.
            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                var existente = _pagosDao.ObtenerPagoAbonadoDeJugadorEnPeriodo(conexion, transaccion, request.IdJugador, mes, anio);
                if (existente != null)
                {
                    throw new CobroInvalidoException(
                        $"Ya existe un pago registrado para este jugador en {request.Periodo} {anio} (pago #{existente.IdPago}).");
                }

                var fechaPago = DateTime.Now.Date; // PAGOS.fecha_pago es DATE: no admite componente de hora

                var pago = new Pago
                {
                    IdJugador = request.IdJugador,
                    MontoBase = request.Monto,
                    MontoFinal = request.Monto,
                    MetodoPago = request.MetodoPago,
                    FechaPago = fechaPago,
                    FechaVencimiento = new DateTime(anio, mes, 1), // qué período cubre esta cuota
                    Estado = true
                };

                int idPago = _pagosDao.InsertarPago(conexion, transaccion, pago);

                return new RegistrarPagoResultado
                {
                    IdPago = idPago,
                    IdJugador = request.IdJugador,
                    Periodo = request.Periodo,
                    Monto = request.Monto,
                    MetodoPago = request.MetodoPago,
                    FechaPago = fechaPago
                };
            });
        }

        public CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request)
        {
            var idsUnicos = ValidarSolicitudDeCobro(request);

            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                var pagos = _pagosDao.ObtenerPagosPorId(conexion, transaccion, idsUnicos);
                ValidarPagosEncontrados(idsUnicos, pagos);

                var fechaPago = DateTime.Now.Date;
                var montoTotal = pagos.Sum(p => p.MontoFinal);

                _pagosDao.MarcarPagosComoAbonados(conexion, transaccion, idsUnicos, fechaPago, request.MetodoPago);

                return new CobrarPagosPendientesResultado
                {
                    PagosAbonados = idsUnicos,
                    MontoTotal = montoTotal,
                    FechaPago = fechaPago
                };
            });
        }

        public IReadOnlyList<PendienteJugador> ObtenerPendientes() => _pagosDao.ObtenerPendientesAgrupados();

        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => _pagosDao.ObtenerUltimosPagos(top <= 0 ? 10 : top);

        public ResumenPagos ObtenerResumen() => _pagosDao.ObtenerResumen();

        private static int ValidarSolicitudDeRegistro(RegistrarPagoRequest request)
        {
            if (request.IdJugador <= 0)
            {
                throw new CobroInvalidoException("Debe indicar un jugador válido.");
            }

            if (!MesesPorNombre.TryGetValue((request.Periodo ?? string.Empty).Trim(), out int mes))
            {
                throw new CobroInvalidoException($"Período inválido: '{request.Periodo}'.");
            }

            if (request.Monto <= 0)
            {
                throw new CobroInvalidoException("El monto debe ser mayor a cero.");
            }

            if (!MetodosPago.EsValido(request.MetodoPago))
            {
                throw new CobroInvalidoException(
                    $"Método de pago inválido: '{request.MetodoPago}'. Valores permitidos: {string.Join(", ", MetodosPago.Validos)}.");
            }

            return mes;
        }

        private static List<int> ValidarSolicitudDeCobro(CobrarPagosPendientesRequest request)
        {
            if (request.IdsPago == null || request.IdsPago.Count == 0)
            {
                throw new CobroInvalidoException("Debe indicar al menos un pago a cobrar.");
            }

            if (!MetodosPago.EsValido(request.MetodoPago))
            {
                throw new CobroInvalidoException(
                    $"Método de pago inválido: '{request.MetodoPago}'. Valores permitidos: {string.Join(", ", MetodosPago.Validos)}.");
            }

            return request.IdsPago.Distinct().ToList();
        }

        // Bloqueo de re-cobro: se valida DENTRO de la transacción, sobre filas ya lockeadas por
        // ObtenerPagosPorId (WITH UPDLOCK), no antes de abrirla.
        private static void ValidarPagosEncontrados(List<int> idsSolicitados, IReadOnlyList<Pago> pagosEncontrados)
        {
            var idsFaltantes = idsSolicitados.Except(pagosEncontrados.Select(p => p.IdPago)).ToList();
            if (idsFaltantes.Count > 0)
            {
                throw new CobroInvalidoException($"No existen los siguientes pagos: {string.Join(", ", idsFaltantes)}.");
            }

            var idsYaAbonados = pagosEncontrados.Where(p => p.Estado).Select(p => p.IdPago).ToList();
            if (idsYaAbonados.Count > 0)
            {
                throw new CobroInvalidoException($"Los siguientes pagos ya fueron abonados: {string.Join(", ", idsYaAbonados)}.");
            }
        }
    }
}
