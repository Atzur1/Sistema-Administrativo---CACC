using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class JugadoresController : ControllerBase
    {
        private readonly IJugadoresDao _jugadoresDao;
        private readonly IPagosDao _pagosDao;

        public JugadoresController(IJugadoresDao jugadoresDao, IPagosDao pagosDao)
        {
            _jugadoresDao = jugadoresDao;
            _pagosDao = pagosDao;
        }

        // GET api/jugadores -> lista completa (nombre, dni, categoría) para el buscador del form de pagos
        [HttpGet]
        public IActionResult ListarJugadores()
        {
            try
            {
                return Ok(_jugadoresDao.ListarJugadores());
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener los jugadores.", error = ex.Message });
            }
        }

        // GET api/jugadores/{id} -> datos básicos para el header del perfil de jugador
        [HttpGet("{id}")]
        public IActionResult ObtenerJugador(int id)
        {
            try
            {
                var jugador = _jugadoresDao.ObtenerJugadorPorId(id);
                if (jugador == null)
                {
                    return NotFound(new { exito = false, mensaje = "Jugador no encontrado." });
                }

                return Ok(jugador);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el jugador.", error = ex.Message });
            }
        }

        // GET api/jugadores/{id}/historial-pagos?page=1&pageSize=10 -> historial paginado, más reciente primero
        [HttpGet("{id}/historial-pagos")]
        public IActionResult ObtenerHistorialPagos(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            try
            {
                if (_jugadoresDao.ObtenerJugadorPorId(id) == null)
                {
                    return NotFound(new { exito = false, mensaje = "Jugador no encontrado." });
                }

                page = page < 1 ? 1 : page;
                pageSize = pageSize is < 1 or > 100 ? 10 : pageSize;

                return Ok(_pagosDao.ObtenerHistorialPagos(id, page, pageSize));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el historial de pagos.", error = ex.Message });
            }
        }
    }
}
