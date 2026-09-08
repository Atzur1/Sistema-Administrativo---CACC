using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class EstadisticasController : ControllerBase
    {
        private readonly IEstadisticasDao _estadisticasDao;

        public EstadisticasController(IEstadisticasDao estadisticasDao)
        {
            _estadisticasDao = estadisticasDao;
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
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el resumen general.", error = ex.Message });
            }
        }
    }
}
