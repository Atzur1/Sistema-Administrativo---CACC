using ApiGestion.Models;
using DaoLibrary.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1")]
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
        [HttpGet("historial")]
        public IActionResult ObtenerHistorial()
        {
            return Ok(_arancelesService.ObtenerHistorial());
        }

        // GET api/aranceles/resumen -> header (arancel masculino/femenino vigente, próximo cambio)
        [HttpGet("resumen")]
        public IActionResult ObtenerResumen()
        {
            return Ok(_arancelesService.ObtenerResumen());
        }

        // POST api/aranceles/programar -> form "Programar nuevo arancel"
        // Body: { "genero": "Masculino", "monto": 92000, "vigenteDesde": "2026-09-24" }
        [HttpPost("programar")]
        public IActionResult ProgramarArancel([FromBody] ProgramarArancelRequestDto request)
        {
            try
            {
                _arancelesService.ProgramarArancel(new ProgramarArancelRequest
                {
                    Genero = request.Genero,
                    Monto = request.Monto,
                    VigenteDesde = request.VigenteDesde
                });

                return Ok(new { exito = true, mensaje = "Arancel programado correctamente." });
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
    }
}
