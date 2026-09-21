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
                // total o parcial. Antes de validar el monto, se busca si el jugador tiene un
                // beneficio de Becados y Descuentos activo para este período — no importa que la
                // cuota ya estuviera cargada antes de asignárselo, se aplica igual acá.
                var descuento = _pagosDao.ObtenerDescuentoAplicableEnPeriodo(conexion, transaccion, request.IdJugador, pendiente.FechaVencimiento!.Value);
                var saldoAjustado = CalcularSaldoAjustado(pendiente.MontoFinal, pendiente.MontoBase, descuento);

                if (saldoAjustado <= 0)
                {
                    throw new CobroInvalidoException(
                        $"La cuota de {request.Periodo} {anio} de este jugador ya está cubierta por un beneficio de Becados y Descuentos, no hay nada que cobrar.");
                }

                // No se admite pagar de más (el vuelto no existe acá) — contra el saldo YA
                // ajustado por el beneficio, no contra el monto_final crudo de la cuota.
                if (request.Monto > saldoAjustado)
                {
                    throw new CobroInvalidoException(
                        $"El monto supera el saldo pendiente (${saldoAjustado:N0}, ya con el beneficio de Becados y Descuentos aplicado) de este jugador para {request.Periodo} {anio}.");
                }

                // montoBaseCuota: se guarda el ORIGINAL de la cuota (no lo que se paga en este
                // abono puntual) para que el Historial de Pagos pueda mostrar más adelante "Cuota:
                // $10.000" aunque la cuota pendiente ya se haya borrado al completarse.
                var resultado = InsertarAbono(conexion, transaccion, request, pendiente.FechaVencimiento!.Value, descuento?.IdJugadorDescuento, pendiente.MontoBase);

                var saldoRestanteAjustado = saldoAjustado - request.Monto;
                if (saldoRestanteAjustado <= 0)
                {
                    // Cubrió el total (ya con el beneficio aplicado): la cuota pendiente ya cumplió
                    // su función, se borra. La "prueba" de que está pagada queda en el/los abono(s)
                    // insertados arriba.
                    _pagosDao.EliminarPago(conexion, transaccion, pendiente.IdPago);
                }
                else
                {
                    // monto_final sigue guardando el saldo crudo (sin el beneficio restado): el
                    // beneficio se vuelve a recalcular la próxima vez que se lea/cobre esta cuota.
                    _pagosDao.ActualizarSaldoPendiente(conexion, transaccion, pendiente.IdPago, pendiente.MontoFinal - request.Monto);
                }

                return resultado;
            });
        }

        // Inserta la fila de PAGOS que representa la plata efectivamente recibida (Estado = true),
        // sea un pago directo (sin cuota previa) o un abono contra una cuota pendiente.
        // idJugadorDescuento queda de rastro de qué beneficio (si hubo uno) se le aplicó a este abono.
        // montoBaseCuota: el monto ORIGINAL de la cuota completa (antes de abonos/beneficio); si es
        // null (pago directo sin cuota previa) se usa el propio monto del abono.
        private RegistrarPagoResultado InsertarAbono(SqlConnection conexion, SqlTransaction transaccion, RegistrarPagoRequest request, DateTime fechaVencimiento, int? idJugadorDescuento = null, decimal? montoBaseCuota = null)
        {
            var fechaPago = DateTime.Now.Date; // PAGOS.fecha_pago es DATE: no admite componente de hora

            var pago = new Pago
            {
                IdJugador = request.IdJugador,
                MontoBase = montoBaseCuota ?? request.Monto,
                IdJugadorDescuento = idJugadorDescuento,
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
        public IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null) => _pagosDao.ObtenerPendientesAgrupados(idCategoria);

        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors)
        {
            return _pagosDao.GetPlayerAccounts(onlyDebtors);
        }

        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => _pagosDao.ObtenerUltimosPagos(top <= 0 ? 10 : top);

        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => _pagosDao.ObtenerDeudaDetalle(idJugador);

        public ResumenPagos ObtenerResumen() => _pagosDao.ObtenerResumen();

        // Cuánto queda realmente pendiente de una cuota tras aplicar el beneficio (si tiene uno):
        // "%" descuenta ese porcentaje del monto ORIGINAL de la cuota, "$" descuenta un monto fijo.
        // Nunca negativo (un beneficio no puede generar saldo a favor).
        private static decimal CalcularSaldoAjustado(decimal montoFinal, decimal montoBase, DescuentoAplicable? descuento)
        {
            if (descuento == null)
            {
                return montoFinal;
            }

            decimal ajustado = descuento.TipoValor == "%"
                ? montoFinal - (montoBase * (descuento.Porcentaje ?? 0) / 100m)
                : montoFinal - (descuento.MontoFijo ?? 0);

            return ajustado < 0 ? 0 : ajustado;
        }

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
