using ApiGestion.Models;
using DaoLibrary.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    // Sin [Authorize] a nivel de clase: se combinaría con AND contra el de cada
    // acción y dejaría afuera al rol que no está en ambos (ver ReportesController).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ArancelesController : ControllerBase
    {
        private readonly IArancelesService _arancelesService;
        private readonly ILogger<ArancelesController> _logger;

        public ArancelesController(IArancelesService arancelesService, ILogger<ArancelesController> logger)
        {
            _arancelesService = arancelesService;
            _logger = logger;
        }

        // GET api/aranceles/historial -> tabla "Historial y aranceles programados"
        [Authorize(Roles = "1")]
        [HttpGet("historial")]
        public IActionResult ObtenerHistorial()
        {
            return Ok(_arancelesService.ObtenerHistorial());
        }

        // GET api/aranceles/resumen -> header (arancel masculino/femenino vigente, próximo cambio).
        // También la usa "Cuotas y Pagos" (rol 2) para mostrar el monto de cuota vigente.
        [Authorize(Roles = "1,2")]
        [HttpGet("resumen")]
        public IActionResult ObtenerResumen()
        {
            return Ok(_arancelesService.ObtenerResumen());
        }

        // POST api/aranceles/programar -> form "Programar nuevo arancel"
        // Body: { "genero": "Masculino", "monto": 92000, "vigenteDesde": "2026-09-24" }
        //    o: { "idCategoria": 5, "monto": 50000, "vigenteDesde": "2026-09-24" } (género o categoría, uno solo)
        [Authorize(Roles = "1")]
        [HttpPost("programar")]
        public IActionResult ProgramarArancel([FromBody] ProgramarArancelRequestDto request)
        {
            try
            {
                ProgramarArancelResultado resultado = _arancelesService.ProgramarArancel(new ProgramarArancelRequest
                {
                    Genero = request.Genero,
                    IdCategoria = request.IdCategoria,
                    Monto = request.Monto,
                    VigenteDesde = request.VigenteDesde
                });

                string mensaje = "Arancel programado correctamente.";
                if (resultado.CuotasReemitidas > 0)
                {
                    mensaje += $" Se volvieron a emitir {resultado.CuotasReemitidas} cuota{(resultado.CuotasReemitidas == 1 ? "" : "s")} pendiente{(resultado.CuotasReemitidas == 1 ? "" : "s")} del mes en curso con el monto nuevo.";
                }
                if (resultado.CuotasConPagosConservadas > 0)
                {
                    mensaje += $" {resultado.CuotasConPagosConservadas} cuota{(resultado.CuotasConPagosConservadas == 1 ? "" : "s")} del mes ya tenía{(resultado.CuotasConPagosConservadas == 1 ? "" : "n")} pagos y se mantuvo{(resultado.CuotasConPagosConservadas == 1 ? "" : "ieron")} sin cambios.";
                }

                return Ok(new { exito = true, mensaje });
            }
            catch (ArancelInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al programar el arancel");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al programar el arancel." });
            }
        }

        // DELETE api/aranceles/{id} -> botón "Cancelar" del historial. Quita el arancel y las cuotas
        // pendientes sin pagos emitidas con él; si alguna ya tiene pagos, se rechaza (400).
        [Authorize(Roles = "1")]
        [HttpDelete("{idArancel:int}")]
        public IActionResult CancelarArancel(int idArancel)
        {
            try
            {
                CancelarArancelResultado resultado = _arancelesService.CancelarArancel(idArancel);

                string mensaje = resultado.CuotasEliminadas > 0
                    ? $"Arancel cancelado. Se quitaron {resultado.CuotasEliminadas} cuota{(resultado.CuotasEliminadas == 1 ? "" : "s")} pendiente{(resultado.CuotasEliminadas == 1 ? "" : "s")} sin pagos."
                    : "Arancel cancelado.";
                mensaje += " Las cuotas se vuelven a emitir al programar un arancel nuevo o en la próxima ejecución automática.";

                return Ok(new { exito = true, mensaje });
            }
            catch (ArancelInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al cancelar el arancel {IdArancel}", idArancel);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al cancelar el arancel." });
            }
        }
    }
}
