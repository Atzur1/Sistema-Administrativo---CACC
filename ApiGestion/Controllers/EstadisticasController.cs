using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1")]
    public class EstadisticasController : ControllerBase
    {
        private readonly IEstadisticasDao _estadisticasDao;
        private readonly ILogger<EstadisticasController> _logger;

        public EstadisticasController(IEstadisticasDao estadisticasDao, ILogger<EstadisticasController> logger)
        {
            _estadisticasDao = estadisticasDao;
            _logger = logger;
        }

        // GET api/estadisticas/resumen-general -> todo lo que necesita el dashboard "Resumen General"
        [HttpGet("resumen-general")]
        public IActionResult ObtenerResumenGeneral()
        {
            try
            {
                return Ok(_estadisticasDao.ObtenerResumenGeneral());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al obtener el resumen general");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el resumen general." });
            }
        }
    }
}
