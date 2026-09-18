using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.Data.SqlClient;

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
            var (mes, anio) = ValidarSolicitudDeRegistro(request);

            // Todo corre dentro de una única transacción: la validación de re-cobro (con lock) y el
            // insert son atómicos, así que ningún request concurrente puede colarse entre medio.
            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                var pendiente = _pagosDao.ObtenerPagoPendienteDeJugadorEnPeriodo(conexion, transaccion, request.IdJugador, mes, anio);

                if (pendiente == null)
                {
                    // Sin cuota generada para este período (ej. todavía no había arancel vigente
                    // cuando se armó la lista de pendientes): comportamiento de siempre, pago
                    // directo ya abonado, bloqueando duplicados.
                    var existente = _pagosDao.ObtenerPagoAbonadoDeJugadorEnPeriodo(conexion, transaccion, request.IdJugador, mes, anio);
                    if (existente != null)
                    {
                        throw new CobroInvalidoException(
                            $"Ya existe un pago registrado para este jugador en {request.Periodo} {anio} (pago #{existente.IdPago}).");
                    }

                    return InsertarAbono(conexion, transaccion, request, new DateTime(anio, mes, 1));
                }

                // Hay una cuota pendiente para este período: este pago es un abono contra ella,
                // total o parcial. No se admite pagar de más (el vuelto no existe acá).
                if (request.Monto > pendiente.MontoFinal)
                {
                    throw new CobroInvalidoException(
                        $"El monto supera el saldo pendiente (${pendiente.MontoFinal:N0}) de este jugador para {request.Periodo} {anio}.");
                }

                var resultado = InsertarAbono(conexion, transaccion, request, pendiente.FechaVencimiento!.Value);

                var saldoRestante = pendiente.MontoFinal - request.Monto;
                if (saldoRestante <= 0)
                {
                    // Cubrió el total: la cuota pendiente ya cumplió su función, se borra. La
                    // "prueba" de que está pagada queda en el/los abono(s) insertados arriba.
                    _pagosDao.EliminarPago(conexion, transaccion, pendiente.IdPago);
                }
                else
                {
                    _pagosDao.ActualizarSaldoPendiente(conexion, transaccion, pendiente.IdPago, saldoRestante);
                }

                return resultado;
            });
        }

        // Inserta la fila de PAGOS que representa la plata efectivamente recibida (Estado = true),
        // sea un pago directo (sin cuota previa) o un abono contra una cuota pendiente.
        private RegistrarPagoResultado InsertarAbono(SqlConnection conexion, SqlTransaction transaccion, RegistrarPagoRequest request, DateTime fechaVencimiento)
        {
            var fechaPago = DateTime.Now.Date; // PAGOS.fecha_pago es DATE: no admite componente de hora

            var pago = new Pago
            {
                IdJugador = request.IdJugador,
                MontoBase = request.Monto,
                MontoFinal = request.Monto,
                MetodoPago = request.MetodoPago,
                FechaPago = fechaPago,
                FechaVencimiento = fechaVencimiento, // qué período cubre este abono
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

        // 100% manual: las cuotas solo existen para los meses en los que se cargó un arancel
        // (ver ArancelesService.ProgramarArancel). Acá no se genera nada por fecha de hoy.
        public IReadOnlyList<PendienteJugador> ObtenerPendientes() => _pagosDao.ObtenerPendientesAgrupados();

        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => _pagosDao.ObtenerUltimosPagos(top <= 0 ? 10 : top);

        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => _pagosDao.ObtenerDeudaDetalle(idJugador);

        public ResumenPagos ObtenerResumen() => _pagosDao.ObtenerResumen();

        private static (int mes, int anio) ValidarSolicitudDeRegistro(RegistrarPagoRequest request)
        {
            if (request.IdJugador <= 0)
            {
                throw new CobroInvalidoException("Debe indicar un jugador válido.");
            }

            if (!MesesPorNombre.TryGetValue((request.Periodo ?? string.Empty).Trim(), out int mes))
            {
                throw new CobroInvalidoException($"Período inválido: '{request.Periodo}'.");
            }

            // Rango amplio a propósito (no solo año actual +- 2): el form limita las opciones,
            // pero esto es la última barrera del lado del servidor contra un año absurdo.
            if (request.Anio < 2000 || request.Anio > DateTime.Now.Year + 1)
            {
                throw new CobroInvalidoException($"Año inválido: '{request.Anio}'.");
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

            return (mes, request.Anio);
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
