using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    // HU-020: catálogo de categorías para el selector de "Deudas y Morosidad".
    // Separado de PagosController porque no depende de la deuda de nadie — lista
    // las 13 categorías del club siempre, tengan o no jugadores morosos.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1,2")]
    public class CategoriasController : ControllerBase
    {
        private readonly ICategoriasDao _categoriasDao;

        public CategoriasController(ICategoriasDao categoriasDao)
        {
            _categoriasDao = categoriasDao;
        }

        // GET api/categorias
        [HttpGet]
        public IActionResult ObtenerTodas()
        {
            return Ok(_categoriasDao.ObtenerTodas());
        }
    }
}
