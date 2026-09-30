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
            return Ok(_pagosService.ObtenerDeudaPorCategoria(anio ?? DateTime.Now.Year, mes));
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
