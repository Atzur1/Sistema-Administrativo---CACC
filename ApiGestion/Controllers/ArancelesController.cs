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
    public class ArancelesController : ControllerBase
    {
        private readonly IArancelesService _arancelesService;

        public ArancelesController(IArancelesService arancelesService)
        {
            _arancelesService = arancelesService;
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
                return StatusCode(500, new { exito = false, mensaje = "Error interno al programar el arancel.", error = ex.Message });
            }
        }
    }
}
