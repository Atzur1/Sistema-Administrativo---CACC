using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    // HU-018: panel de Recaudación Mensual. Controller propio y no un método más
    // en PagosController porque no mueve plata ni toca cuotas: solo lee y agrega.
    //
    // Rol 1 (SuperAdmin) únicamente, igual que el ítem del sidebar y el
    // superAdminGuard de la ruta en el frontend: así no se entra por la API a lo
    // que la pantalla no muestra.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1")]
    public class RecaudacionController : ControllerBase
    {
        private readonly IRecaudacionDao _recaudacionDao;
        private readonly ILogger<RecaudacionController> _logger;

        public RecaudacionController(IRecaudacionDao recaudacionDao, ILogger<RecaudacionController> logger)
        {
            _recaudacionDao = recaudacionDao;
            _logger = logger;
        }

        // GET api/recaudacion?anio=2026&mes=10
        // GET api/recaudacion?anio=2026&mes=10&idCategoria=5 -> acota a una categoría
        // 200: { total, efectivo, transferencia, pagos, pagosEfectivo, pagosTransferencia, detalle: [...] }
        [HttpGet]
        public IActionResult ObtenerDelPeriodo(
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? idCategoria = null)
        {
            if (mes < 1 || mes > 12)
            {
                return BadRequest(new { exito = false, mensaje = "El mes debe estar entre 1 y 12." });
            }

            // Cota amplia a propósito: no es validación de negocio, solo evita
            // que un año absurdo llegue a la consulta.
            if (anio < 2000 || anio > 2100)
            {
                return BadRequest(new { exito = false, mensaje = "El año no es válido." });
            }

            try
            {
                RecaudacionPeriodo periodo = _recaudacionDao.ObtenerDelPeriodo(anio, mes, idCategoria);
                return Ok(RecaudacionResponseDTO.FromEntity(periodo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al obtener la recaudación de {Mes:00}/{Anio}", mes, anio);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener la recaudación." });
            }
        }
    }
}
