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
    [Authorize]
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
        [HttpGet("pendientes")]
        public IActionResult ObtenerPendientes()
        {
            return Ok(_pagosService.ObtenerPendientes());
        }

        // GET api/pagos/player-accounts?onlyDebtors=true -> roster of Cuotas y Pagos (HU-029).
        // Without the flag it returns every player; with it, only the ones with a balance above
        // zero, by the same rule as the "Deuda Global Total" indicator.
        // 200:   [{ playerId, firstName, lastName, dni, category, amountOwed, pendingInstallments }]
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
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener los jugadores.", error = ex.Message });
            }
        }

        // GET api/pagos/recientes?top=10 -> panel "Últimos pagos"
        [HttpGet("recientes")]
        public IActionResult ObtenerUltimosPagos([FromQuery] int top = 10)
        {
            return Ok(_pagosService.ObtenerUltimosPagos(top));
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
                    MetodoPago = request.MetodoPago
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
                return StatusCode(500, new { exito = false, mensaje = "Error interno al registrar el pago.", error = ex.Message });
            }
        }

        // POST api/pagos/cobro -> cobro en lote de pagos pendientes preexistentes (PAGOS.estado = 0)
        // Body:  { "idsPago": [45, 46], "metodoPago": "Efectivo" }
        // 200:   { exito, pagosAbonados, montoTotal, fechaPago, mensaje }
        // 400:   pago inexistente, ya abonado, o método inválido
        [HttpPost("cobro")]
        public IActionResult CobrarPagosPendientes([FromBody] RegistrarCobroRequestDto request)
        {
            try
            {
                var resultado = _pagosService.CobrarPagosPendientes(new CobrarPagosPendientesRequest
                {
                    IdsPago = request.IdsPago,
                    MetodoPago = request.MetodoPago
                });

                return Ok(new
                {
                    exito = true,
                    pagosAbonados = resultado.PagosAbonados,
                    montoTotal = resultado.MontoTotal,
                    fechaPago = resultado.FechaPago,
                    mensaje = "Cobro registrado correctamente."
                });
            }
            catch (CobroInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error interno al procesar el cobro.", error = ex.Message });
            }
        }

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
