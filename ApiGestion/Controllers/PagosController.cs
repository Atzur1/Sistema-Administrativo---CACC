using ApiGestion.Models;
using DaoLibrary.Exceptions;
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
        private readonly IPagosService _pagosService;

        public PagosController(IPagosService pagosService)
        {
            _pagosService = pagosService;
        }

        // GET api/pagos/resumen -> métricas del header (recaudado del año, pagos del mes, pendientes)
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

        // POST api/pagos/registrar -> form "Registrar pago" (jugador + período + monto + método)
        // Body:  { "idJugador": 12, "periodo": "Marzo", "monto": 85000, "metodoPago": "Transferencia" }
        // 200:   { exito, idPago, idJugador, periodo, monto, metodoPago, fechaPago, mensaje }
        // 400:   jugador/período/monto/método inválido, o ya existe un pago de ese jugador en ese período
        [HttpPost("registrar")]
        public IActionResult RegistrarPago([FromBody] RegistrarPagoRequestDto request)
        {
            try
            {
                var resultado = _pagosService.RegistrarPago(new RegistrarPagoRequest
                {
                    IdJugador = request.IdJugador,
                    Periodo = request.Periodo,
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
    }
}
