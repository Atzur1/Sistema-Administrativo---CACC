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

        private static readonly string[] NombresMes =
        {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        // Tope defensivo de cuotas por cobro (igual que el DTO).
        private const int MaximoCuotasPorCobro = 100;

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
                if (!_pagosDao.ExisteJugador(conexion, transaccion, request.IdJugador))
                {
                    throw new CobroInvalidoException($"El jugador #{request.IdJugador} no existe.");
                }

                var pendiente = _pagosDao.ObtenerPagoPendienteDeJugadorEnPeriodo(conexion, transaccion, request.IdJugador, mes, anio);

                if (pendiente == null)
                {
                    var existente = _pagosDao.ObtenerPagoAbonadoDeJugadorEnPeriodo(conexion, transaccion, request.IdJugador, mes, anio);
                    if (existente != null)
                    {
                        throw new CobroInvalidoException(
                            $"Ya existe un pago registrado para este jugador en {request.Periodo} {anio} (pago #{existente.IdPago}).");
                    }

                    // Sin cuota emitida para el período (un mes futuro que se paga por adelantado, o uno que
                    // el generador todavía no alcanzó): se emite primero con el arancel que le corresponde al
                    // jugador y el pago se registra contra ella. Antes se aceptaba un pago directo por
                    // CUALQUIER monto, y como el generador saltea a quien ya tiene una fila en el mes, un pago
                    // de $1 dejaba ese mes saldado para siempre.
                    if (!_pagosDao.EmitirCuotaDeJugador(conexion, transaccion, request.IdJugador, mes, anio))
                    {
                        throw new CobroInvalidoException(
                            $"No se puede registrar un pago para {request.Periodo} {anio}: no hay un arancel vigente para este jugador en ese mes, " +
                            "o el jugador no estaba en el club (antes de su alta o después de su baja).");
                    }

                    pendiente = _pagosDao.ObtenerPagoPendienteDeJugadorEnPeriodo(conexion, transaccion, request.IdJugador, mes, anio)
                        ?? throw new InvalidOperationException("La cuota recién emitida no se encontró.");
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

        // Anula un abono cargado por error (solo SuperAdmin, con motivo). El abono sale de PAGOS y queda
        // registrado en PAGOS_ANULADOS; su monto vuelve al saldo de la cuota o inscripción que cubría:
        // si la deuda seguía abierta se le suma al pendiente, y si ya estaba saldada se vuelve a abrir
        // por lo que falta (monto original menos los demás abonos). El beneficio de Becados y Descuentos
        // se sigue aplicando al leer, igual que siempre.
        public AnularPagoResultado AnularPago(AnularPagoRequest request)
        {
            string motivo = (request.Motivo ?? string.Empty).Trim();
            if (request.IdPago <= 0)
            {
                throw new CobroInvalidoException("Debe indicar un pago válido.");
            }
            if (motivo.Length < 5 || motivo.Length > 500)
            {
                throw new CobroInvalidoException("Indicá el motivo de la anulación (entre 5 y 500 caracteres).");
            }
            if (request.IdUsuarioAnulacion <= 0)
            {
                throw new CobroInvalidoException("No se pudo identificar al usuario que anula el pago.");
            }

            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                var abono = _pagosDao.ObtenerPagoParaAnular(conexion, transaccion, request.IdPago)
                    ?? throw new CobroInvalidoException($"El pago #{request.IdPago} no existe o ya fue anulado.");
                if (!abono.Estado)
                {
                    throw new CobroInvalidoException($"El registro #{request.IdPago} es una deuda pendiente, no un pago: no se puede anular.");
                }

                // Pagos históricos sin período cargado: no hay a qué cuota devolverle el saldo.
                var filas = abono.FechaVencimiento.HasValue || abono.Concepto == "Inscripcion"
                    ? _pagosDao.ObtenerFilasDeLaMismaDeuda(conexion, transaccion, abono)
                    : new List<Pago> { abono };

                _pagosDao.RegistrarAnulacionYEliminarAbono(conexion, transaccion, abono, motivo, request.IdUsuarioAnulacion);

                decimal saldoReabierto = 0;
                if (abono.FechaVencimiento.HasValue)
                {
                    var pendiente = filas.FirstOrDefault(f => !f.Estado);
                    if (pendiente != null)
                    {
                        saldoReabierto = pendiente.MontoFinal + abono.MontoFinal;
                        _pagosDao.ActualizarSaldoPendiente(conexion, transaccion, pendiente.IdPago, saldoReabierto);
                    }
                    else
                    {
                        decimal otrosAbonos = filas.Where(f => f.Estado && f.IdPago != abono.IdPago).Sum(f => f.MontoFinal);
                        saldoReabierto = abono.MontoBase - otrosAbonos;
                        if (saldoReabierto > 0)
                        {
                            _pagosDao.InsertarPago(conexion, transaccion, new Pago
                            {
                                IdJugador = abono.IdJugador,
                                MontoBase = abono.MontoBase,
                                MontoFinal = saldoReabierto,
                                FechaVencimiento = abono.FechaVencimiento,
                                Estado = false,
                                Concepto = abono.Concepto
                            });
                        }
                        else
                        {
                            saldoReabierto = 0;
                        }
                    }
                }

                return new AnularPagoResultado
                {
                    IdPago = abono.IdPago,
                    IdJugador = abono.IdJugador,
                    MontoAnulado = abono.MontoFinal,
                    SaldoReabierto = saldoReabierto
                };
            });
        }

        // Inserta la fila de PAGOS que representa la plata efectivamente recibida (Estado = true),
        // como abono contra una cuota pendiente. idJugadorDescuento queda de rastro de qué beneficio
        // (si hubo uno) se le aplicó. montoBaseCuota: el monto ORIGINAL de la cuota completa (antes de
        // abonos/beneficio).
        private RegistrarPagoResultado InsertarAbono(SqlConnection conexion, SqlTransaction transaccion, RegistrarPagoRequest request, DateTime fechaVencimiento, int? idJugadorDescuento = null, decimal? montoBaseCuota = null)
        {
            var fechaPago = RelojNegocio.Hoy; // PAGOS.fecha_pago es DATE: no admite componente de hora

            var pago = new Pago
            {
                IdJugador = request.IdJugador,
                MontoBase = montoBaseCuota ?? request.Monto,
                IdJugadorDescuento = idJugadorDescuento,
                MontoFinal = request.Monto,
                MetodoPago = request.MetodoPago,
                FechaPago = fechaPago,
                FechaVencimiento = fechaVencimiento, // qué período cubre este abono
                Estado = true,
                IdUsuarioRegistro = request.IdUsuarioRegistro
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

        // HU-025: todo el cobro corre en UNA transacción. Las cuotas se leen con UPDLOCK/HOLDLOCK y
        // recién ahí se validan, así que si otro administrador las cobró segundos antes (pantalla
        // desactualizada) o las está cobrando ahora (espera su commit), este intento las ve pagadas
        // y se rechaza. Cualquier excepción, de negocio o de base, revierte el lote completo.
        public CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request)
        {
            var (ids, metodoPago) = ValidarSolicitudDeCobro(request);

            return _transactionRunner.EjecutarEnTransaccion((conexion, transaccion) =>
            {
                if (!_pagosDao.ExisteJugador(conexion, transaccion, request.IdJugador))
                {
                    throw new CobroInvalidoException($"El jugador #{request.IdJugador} no existe.");
                }

                var pagos = _pagosDao.ObtenerPagosPorId(conexion, transaccion, ids);
                ValidarPagosEncontrados(request.IdJugador, ids, pagos);

                var cuotas = new List<CuotaCobrada>();
                foreach (var pago in pagos.OrderBy(p => p.FechaVencimiento).ThenBy(p => p.IdPago))
                {
                    // Monto histórico: el que quedó guardado en la cuota al emitirse (menos abonos
                    // parciales previos), nunca el arancel vigente hoy. Solo se descuenta el
                    // beneficio de Becados y Descuentos que cubra ese período, igual que al leer.
                    var montoCobrar = pago.MontoFinal;
                    if (pago.FechaVencimiento.HasValue)
                    {
                        var descuento = _pagosDao.ObtenerDescuentoAplicableEnPeriodo(conexion, transaccion, pago.IdJugador, pago.FechaVencimiento.Value);
                        montoCobrar = CalcularSaldoAjustado(pago.MontoFinal, pago.MontoBase, descuento);
                        if (montoCobrar <= 0)
                            throw new CobroInvalidoException($"La cuota de {Periodo(pago.FechaVencimiento)} está cubierta por un beneficio y no corresponde cobrarla.");
                        if (descuento != null)
                            _pagosDao.ActualizarMontoCobroConDescuento(conexion, transaccion, pago.IdPago, descuento.IdJugadorDescuento, montoCobrar);
                    }

                    cuotas.Add(new CuotaCobrada
                    {
                        IdPago = pago.IdPago,
                        Periodo = Periodo(pago.FechaVencimiento),
                        Monto = montoCobrar,
                        Estado = EstadosCuota.Pagado
                    });
                }

                // fecha_pago: el cobro se asienta el día en que se registra (no hay carga
                // retroactiva). La hora exacta de registro la fija SQL Server en el UPDATE.
                var fechaPago = RelojNegocio.Hoy;
                var fechaHoraRegistro = _pagosDao.MarcarPagosComoAbonados(
                    conexion, transaccion, request.IdJugador, ids, fechaPago, metodoPago, request.IdUsuarioRegistro);

                return new CobrarPagosPendientesResultado
                {
                    IdJugador = request.IdJugador,
                    PagosAbonados = ids,
                    Cuotas = cuotas,
                    MontoTotal = cuotas.Sum(c => c.Monto),
                    MetodoPago = metodoPago,
                    FechaPago = fechaPago,
                    FechaHoraRegistro = fechaHoraRegistro
                };
            });
        }

        public IReadOnlyList<CuotaJugador> ObtenerCuotasJugador(int idJugador)
            => ConstruirEstadoDeCuotas(_pagosDao.ObtenerMovimientosCuotas(idJugador), RelojNegocio.Hoy);

        public PlayerStatement? GetPlayerStatement(int playerId)
        {
            var cuenta = _pagosDao.GetPlayerStatementAccount(playerId);
            return cuenta == null ? null : BuildPlayerStatement(cuenta, RelojNegocio.Hoy);
        }

        // HU-024: las cuotas salen de ConstruirEstadoDeCuotas, el mismo cálculo que usa la tabla de
        // cobro (HU-025), así el estado de cuenta y lo que se puede cobrar nunca difieren. El total
        // suma el saldo de las Pendientes y Vencidas, que ya descuenta beneficios y abonos parciales.
        public static PlayerStatement BuildPlayerStatement(PlayerStatementAccount cuenta, DateTime hoy)
        {
            var cuotas = ConstruirEstadoDeCuotas(cuenta.Movements, hoy);
            return new PlayerStatement
            {
                PlayerId = cuenta.PlayerId,
                FirstName = cuenta.FirstName,
                LastName = cuenta.LastName,
                Dni = cuenta.Dni,
                Fees = cuotas,
                TotalDebtAmount = cuotas.Where(c => c.Estado != EstadosCuota.Pagado).Sum(c => c.SaldoPendiente)
            };
        }

        // Agrupa las filas de PAGOS de cada período en una sola cuota. Mientras exista la fila
        // pendiente (estado = 0), la cuota se puede cobrar por su saldo; si ya no existe (se cobró
        // completa por /cobro o por abonos que la cubrieron), está pagada. Vencida = pasó el
        // último día del mes que cubre, el mismo criterio que usa la ficha para "Vencida".
        public static IReadOnlyList<CuotaJugador> ConstruirEstadoDeCuotas(IEnumerable<CuotaMovimiento> movimientos, DateTime hoy)
        {
            return movimientos
                .GroupBy(m => new DateTime(m.FechaVencimiento.Year, m.FechaVencimiento.Month, 1))
                .OrderBy(g => g.Key)
                .Select(periodo =>
                {
                    var pendiente = periodo.FirstOrDefault(m => !m.Estado);
                    var abonos = periodo.Where(m => m.Estado)
                        .OrderBy(m => m.FechaPago ?? DateTime.MinValue).ThenBy(m => m.IdPago).ToList();
                    var ultimoAbono = abonos.LastOrDefault();

                    var cuota = new CuotaJugador
                    {
                        Periodo = Periodo(periodo.Key),
                        FechaVencimiento = periodo.Key,
                        MontoCuota = periodo.Max(m => m.MontoBase),
                        MontoAbonado = abonos.Sum(m => m.MontoFinal),
                        MetodoPago = ultimoAbono?.MetodoPago,
                        FechaPago = ultimoAbono?.FechaPago,
                        MotivoBeneficio = (pendiente ?? ultimoAbono)?.MotivoBeneficio
                    };

                    if (pendiente == null)
                    {
                        cuota.IdPago = ultimoAbono!.IdPago;
                        cuota.Estado = EstadosCuota.Pagado;
                        return cuota;
                    }

                    var ultimoDiaDelMes = periodo.Key.AddMonths(1).AddDays(-1);
                    cuota.IdPago = pendiente.IdPago;
                    cuota.SaldoPendiente = Math.Max(0, pendiente.SaldoAjustado);
                    cuota.CubiertaPorBeneficio = cuota.SaldoPendiente <= 0;
                    cuota.Estado = hoy.Date > ultimoDiaDelMes ? EstadosCuota.Vencido : EstadosCuota.Pendiente;
                    return cuota;
                })
                .ToList();
        }

        private static string Periodo(DateTime? fechaVencimiento) =>
            fechaVencimiento.HasValue ? $"{NombresMes[fechaVencimiento.Value.Month - 1]} {fechaVencimiento.Value.Year}" : "-";

        // Las cuotas las crea GeneradorCuotasMensuales cada mes (repitiendo el arancel vigente) y
        // ArancelesService.ProgramarArancel al cargar un arancel. Acá no se genera nada: solo se lee.
        public IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null) => _pagosDao.ObtenerPendientesAgrupados(idCategoria);

        public IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null) => _pagosDao.ObtenerDeudaPorCategoria(anio, mes);

        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors)
        {
            return _pagosDao.GetPlayerAccounts(onlyDebtors);
        }

        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => _pagosDao.ObtenerUltimosPagos(top <= 0 ? 10 : top);

        public IReadOnlyList<PagoReciente> ObtenerTodosLosPagos() => _pagosDao.ObtenerTodosLosPagos();

        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => _pagosDao.ObtenerDeudaDetalle(idJugador);

        public ResumenPagos ObtenerResumen() => _pagosDao.ObtenerResumen();

        public ResumenPagosHoy ObtenerResumenHoy() => _pagosDao.ObtenerResumenHoy();

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
            if (request.Anio < 2000 || request.Anio > RelojNegocio.Ahora.Year + 1)
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

            // Igual que en /cobro: el operador sale del JWT y es obligatorio para la trazabilidad.
            if (request.IdUsuarioRegistro is null or <= 0)
            {
                throw new CobroInvalidoException("No se pudo identificar al usuario que registra el pago.");
            }

            return (mes, request.Anio);
        }

        private static (List<int> ids, string metodoPago) ValidarSolicitudDeCobro(CobrarPagosPendientesRequest request)
        {
            if (request.IdJugador <= 0)
            {
                throw new CobroInvalidoException("Debe indicar un jugador válido.");
            }

            if (request.IdsPago == null || request.IdsPago.Count == 0)
            {
                throw new CobroInvalidoException("Debe seleccionar al menos una cuota a cobrar.");
            }

            if (request.IdsPago.Count > MaximoCuotasPorCobro || request.IdsPago.Any(id => id <= 0))
            {
                throw new CobroInvalidoException($"El cobro admite hasta {MaximoCuotasPorCobro} cuotas con identificadores válidos.");
            }

            // Un id repetido indica un cliente desincronizado: se rechaza en vez de deduplicarlo en
            // silencio, así el total confirmado en pantalla nunca difiere de lo que se cobra.
            var repetidos = request.IdsPago.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (repetidos.Count > 0)
            {
                throw new CobroInvalidoException($"La solicitud tiene cuotas repetidas: {string.Join(", ", repetidos)}.");
            }

            var metodoPago = MetodosPago.Validos.FirstOrDefault(m =>
                string.Equals(m, request.MetodoPago?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (metodoPago == null)
            {
                throw new CobroInvalidoException(
                    $"Método de pago inválido: '{request.MetodoPago}'. Valores permitidos: {string.Join(", ", MetodosPago.Validos)}.");
            }

            if (request.IdUsuarioRegistro <= 0)
            {
                throw new CobroInvalidoException("No se pudo identificar al usuario que registra el cobro.");
            }

            return (request.IdsPago.ToList(), metodoPago);
        }

        // Bloqueo de re-cobro: se valida DENTRO de la transacción, sobre filas ya lockeadas por
        // ObtenerPagosPorId (WITH UPDLOCK), no antes de abrirla.
        private static void ValidarPagosEncontrados(int idJugador, List<int> idsSolicitados, IReadOnlyList<Pago> pagosEncontrados)
        {
            var idsFaltantes = idsSolicitados.Except(pagosEncontrados.Select(p => p.IdPago)).ToList();
            if (idsFaltantes.Count > 0)
            {
                throw new CobroInvalidoException($"No existen las siguientes cuotas: {string.Join(", ", idsFaltantes)}.");
            }

            var idsDeOtroJugador = pagosEncontrados.Where(p => p.IdJugador != idJugador).Select(p => p.IdPago).ToList();
            if (idsDeOtroJugador.Count > 0)
            {
                throw new CobroInvalidoException($"Las siguientes cuotas no pertenecen al jugador seleccionado: {string.Join(", ", idsDeOtroJugador)}.");
            }

            // La inscripción tiene su propio flujo de cobro (HU-033).
            var idsNoCuota = pagosEncontrados
                .Where(p => !string.Equals(p.Concepto?.Trim(), "Cuota", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.IdPago).ToList();
            if (idsNoCuota.Count > 0)
            {
                throw new CobroInvalidoException($"Los siguientes cargos no son cuotas mensuales: {string.Join(", ", idsNoCuota)}.");
            }

            var idsYaAbonados = pagosEncontrados.Where(p => p.Estado).Select(p => p.IdPago).ToList();
            if (idsYaAbonados.Count > 0)
            {
                throw new CobroInvalidoException($"Las siguientes cuotas ya fueron abonadas: {string.Join(", ", idsYaAbonados)}. Actualizá la ficha y volvé a intentar.");
            }
        }
    }
}
