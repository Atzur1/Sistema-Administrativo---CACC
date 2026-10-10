using ApiGestion.Models;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1,2")]
    public class PagosController : ControllerBase
    {
        private readonly ILogger<PagosController> _logger;
        private readonly IPagosService _pagosService;

        public PagosController(ILogger<PagosController> logger, IPagosService pagosService)
        {
            _logger = logger;
            _pagosService = pagosService;
        }

        // GET api/pagos/resumen -> métricas del header (recaudado del año, pagos del mes, pendientes,
        // deuda global total y cantidad de jugadores morosos — HU-019)
        [HttpGet("resumen")]
        public IActionResult ObtenerResumen()
        {
            return Ok(_pagosService.ObtenerResumen());
        }

        // GET api/pagos/pendientes -> panel "Pendientes de cobro"
        // GET api/pagos/pendientes?idCategoria=5 -> HU-020: acota el padrón a esa categoría/división
        [HttpGet("pendientes")]
        public IActionResult ObtenerPendientes([FromQuery] int? idCategoria = null)
        {
            return Ok(_pagosService.ObtenerPendientes(idCategoria));
        }

        // GET api/pagos/player-accounts?onlyDebtors=true -> pantalla "Ver todos los deudores" (HU-029).
        // Sin el parámetro devuelve el padrón completo; con él, solo los jugadores con saldo mayor
        // a cero, por la misma regla que el indicador "Deuda Global Total".
        // 200:   [{ playerId, firstName, lastName, dni, category, amountOwed, pendingInstallments }]
        // 400:   onlyDebtors no es un booleano
        [HttpGet("player-accounts")]
        public IActionResult GetPlayerAccounts([FromQuery] bool onlyDebtors = false)
        {
            try
            {
                var accounts = _pagosService.GetPlayerAccounts(onlyDebtors);
                _logger.LogInformation("Player accounts returned: {Count} (onlyDebtors: {OnlyDebtors})", accounts.Count, onlyDebtors);

                return Ok(accounts.Select(MapToDto).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "The player accounts could not be read");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener los jugadores." });
            }
        }

        // GET api/pagos/deuda-por-categoria?anio=2026&mes=9 -> deuda de septiembre 2026 por categoría
        // GET api/pagos/deuda-por-categoria?anio=2026 -> deuda de TODO el año 2026 por categoría (mes omitido)
        [HttpGet("deuda-por-categoria")]
        public IActionResult ObtenerDeudaPorCategoria([FromQuery] int? anio = null, [FromQuery] int? mes = null)
        {
            return Ok(_pagosService.ObtenerDeudaPorCategoria(anio ?? RelojNegocio.Ahora.Year, mes));
        }

        // GET api/pagos/recientes?top=10 -> panel "Últimos pagos"
        [HttpGet("recientes")]
        public IActionResult ObtenerUltimosPagos([FromQuery] int top = 10)
        {
            return Ok(_pagosService.ObtenerUltimosPagos(Math.Clamp(top, 1, 1000)));
        }

        // GET api/pagos/resumen-hoy -> banner de "Actividad y Movimientos" (pagos y recaudado de HOY)
        [HttpGet("resumen-hoy")]
        public IActionResult ObtenerResumenHoy()
        {
            return Ok(_pagosService.ObtenerResumenHoy());
        }

        // GET api/pagos/registrados -> tabla "Pagos registrados" de Actividad y Movimientos:
        // límite defensivo de 1000 resultados.
        [HttpGet("registrados")]
        public IActionResult ObtenerTodosLosPagos()
        {
            return Ok(_pagosService.ObtenerUltimosPagos(1000));
        }

        // GET api/pagos/deuda/5 -> detalle de deuda de un jugador (cuotas pendientes + abonos parciales)
        [HttpGet("deuda/{idJugador}")]
        public IActionResult ObtenerDeudaDetalle(int idJugador)
        {
            return Ok(_pagosService.ObtenerDeudaDetalle(idJugador));
        }

        // POST api/pagos/registrar -> form "Registrar pago" (jugador + período + año + monto + método)
        // Body:  { "idJugador": 12, "periodo": "Marzo", "anio": 2026, "monto": 85000, "metodoPago": "Transferencia" }
        // 200:   { exito, idPago, idJugador, periodo, monto, metodoPago, fechaPago, mensaje }
        // 400:   jugador/período/año/monto/método inválido, o ya existe un pago de ese jugador en ese período
        [HttpPost("registrar")]
        public IActionResult RegistrarPago([FromBody] RegistrarPagoRequestDto request)
        {
            if (IdUsuarioAutenticado() == null)
            {
                return Unauthorized(new { exito = false, mensaje = "No se pudo identificar al usuario de la sesión. Volvé a iniciar sesión." });
            }

            try
            {
                var resultado = _pagosService.RegistrarPago(new RegistrarPagoRequest
                {
                    IdJugador = request.IdJugador,
                    Periodo = request.Periodo,
                    Anio = request.Anio,
                    Monto = request.Monto,
                    MetodoPago = request.MetodoPago,
                    IdUsuarioRegistro = IdUsuarioAutenticado()
                });

                return Ok(new
                {
                    exito = true,
                    idPago = resultado.IdPago,
                    idJugador = resultado.IdJugador,
                    periodo = resultado.Periodo,
                    monto = resultado.Monto,
                    metodoPago = resultado.MetodoPago,
                    fechaPago = resultado.FechaPago,
                    mensaje = "Pago registrado correctamente."
                });
            }
            catch (CobroInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al registrar el pago");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al registrar el pago." });
            }
        }

        // GET api/pagos/cuotas/5 -> HU-025: cuotas del jugador con estado Pendiente/Vencido/Pagado
        // para la tabla de selección de la ficha financiera.
        [HttpGet("cuotas/{idJugador}")]
        public IActionResult ObtenerCuotasJugador(int idJugador)
        {
            return Ok(_pagosService.ObtenerCuotasJugador(idJugador));
        }

        // GET api/pagos/jugador/5/estado-de-cuenta -> HU-024: estado de cuenta del jugador con sus
        // cuotas en orden cronológico (estado Pagado/Pendiente/Vencido) y el total a abonar.
        // 404 si el jugador no existe. Roles 1 y 2, como el resto de PagosController.
        [HttpGet("jugador/{idJugador:int}/estado-de-cuenta")]
        public IActionResult GetPlayerStatement(int idJugador)
        {
            try
            {
                PlayerStatement? estado = _pagosService.GetPlayerStatement(idJugador);
                if (estado == null)
                {
                    return NotFound(new { exito = false, mensaje = "Jugador no encontrado." });
                }

                return Ok(new PlayerStatementDto
                {
                    PlayerId = estado.PlayerId,
                    PlayerFullName = $"{estado.LastName}, {estado.FirstName}",
                    Dni = estado.Dni,
                    TotalDebtAmount = estado.TotalDebtAmount,
                    Fees = estado.Fees.Select(cuota => new FeeItemDto
                    {
                        Id = cuota.IdPago,
                        PeriodName = cuota.Periodo,
                        Amount = cuota.MontoCuota,
                        AmountDue = cuota.Estado == EstadosCuota.Pagado ? 0 : cuota.SaldoPendiente,
                        AmountPaid = cuota.MontoAbonado,
                        DueDate = cuota.FechaVencimiento.AddMonths(1).AddDays(-1).ToString("yyyy-MM-dd"),
                        Status = cuota.Estado,
                        PaidAt = cuota.FechaPago?.ToString("yyyy-MM-dd"),
                        PaymentMethod = cuota.MetodoPago,
                        CoveredByBenefit = cuota.CubiertaPorBeneficio,
                        BenefitReason = cuota.MotivoBeneficio
                    }).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al obtener el estado de cuenta del jugador {IdJugador}", idJugador);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el estado de cuenta." });
            }
        }

        // POST api/pagos/cobro -> HU-025: cobro atómico de una o varias cuotas pendientes/vencidas
        // de un jugador (todas o ninguna). El operador sale del JWT, no del body.
        // Body:  { "idJugador": 12, "idsPago": [45, 46], "metodoPago": "Efectivo" }
        // 200:   { exito, idJugador, pagosAbonados, cuotas: [{ idPago, periodo, monto, estado }],
        //          montoTotal, metodoPago, fechaPago, fechaHoraRegistro, estado, mensaje }
        // 400:   jugador inexistente, cuota inexistente/ajena/ya abonada/repetida, o método inválido
        // 401:   el token no identifica al operador
        [HttpPost("cobro")]
        public IActionResult CobrarPagosPendientes([FromBody] RegistrarCobroRequestDto request)
        {
            int? idUsuario = IdUsuarioAutenticado();
            if (idUsuario == null)
            {
                return Unauthorized(new { exito = false, mensaje = "No se pudo identificar al usuario de la sesión. Volvé a iniciar sesión." });
            }

            try
            {
                var resultado = _pagosService.CobrarPagosPendientes(new CobrarPagosPendientesRequest
                {
                    IdJugador = request.IdJugador,
                    IdsPago = request.IdsPago,
                    MetodoPago = request.MetodoPago,
                    IdUsuarioRegistro = idUsuario.Value
                });

                _logger.LogInformation("Cobro registrado: jugador {IdJugador}, cuotas {Cuotas}, usuario {IdUsuario}",
                    resultado.IdJugador, string.Join(",", resultado.PagosAbonados), idUsuario.Value);

                return Ok(new
                {
                    exito = true,
                    idJugador = resultado.IdJugador,
                    pagosAbonados = resultado.PagosAbonados,
                    cuotas = resultado.Cuotas,
                    montoTotal = resultado.MontoTotal,
                    metodoPago = resultado.MetodoPago,
                    fechaPago = resultado.FechaPago,
                    fechaHoraRegistro = resultado.FechaHoraRegistro,
                    estado = EstadosCuota.Pagado,
                    mensaje = "Cobro registrado correctamente."
                });
            }
            catch (CobroInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al procesar el cobro");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al procesar el cobro." });
            }
        }

        // POST api/pagos/45/anular -> anula un pago cargado por error. Solo SuperAdmin.
        // Body:  { "motivo": "Se registró en el jugador equivocado" }
        // 200:   { exito, idPago, idJugador, montoAnulado, saldoReabierto, mensaje }
        // 400:   pago inexistente, ya anulado, es una deuda (no un pago) o motivo inválido
        [Authorize(Roles = "1")]
        [HttpPost("{idPago:int}/anular")]
        public IActionResult AnularPago(int idPago, [FromBody] AnularPagoRequestDto request)
        {
            int? idUsuario = IdUsuarioAutenticado();
            if (idUsuario == null)
            {
                return Unauthorized(new { exito = false, mensaje = "No se pudo identificar al usuario de la sesión. Volvé a iniciar sesión." });
            }

            try
            {
                var resultado = _pagosService.AnularPago(new AnularPagoRequest
                {
                    IdPago = idPago,
                    Motivo = request.Motivo,
                    IdUsuarioAnulacion = idUsuario.Value
                });

                _logger.LogInformation("Pago {IdPago} anulado por el usuario {IdUsuario}", idPago, idUsuario.Value);

                return Ok(new
                {
                    exito = true,
                    idPago = resultado.IdPago,
                    idJugador = resultado.IdJugador,
                    montoAnulado = resultado.MontoAnulado,
                    saldoReabierto = resultado.SaldoReabierto,
                    mensaje = "Pago anulado. Su monto volvió al saldo pendiente."
                });
            }
            catch (CobroInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al anular el pago {IdPago}", idPago);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al anular el pago." });
            }
        }

        // Mismo claim que usa AuditActorMiddleware para la auditoría en SQL Server.
        private int? IdUsuarioAutenticado() =>
            int.TryParse(User?.FindFirst("idUsuario")?.Value, out var id) && id > 0 ? id : null;

        private static PlayerAccountResponseDTO MapToDto(PlayerAccount account)
        {
            return new PlayerAccountResponseDTO
            {
                PlayerId = account.PlayerId,
                FirstName = account.FirstName,
                LastName = account.LastName,
                Dni = account.Dni,
                Category = account.Category,
                AmountOwed = account.AmountOwed,
                PendingInstallments = account.PendingInstallments
            };
        }
    }
}
