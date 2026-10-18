using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    // Aviso del Resumen General (solo SuperAdmin): jugadores sin género cargado, a los que no se les puede emitir la cuota.
    // Solo informa: la persona la carga quien la da de alta, esta pantalla no modifica nada.
    [ApiController]
    [Route("api/jugadores-sin-genero")]
    [Authorize(Roles = "1")]
    public class JugadoresSinGeneroController : ControllerBase
    {
        public const int MaximoListado = 200;

        private readonly IJugadoresSinGeneroDao _dao;
        private readonly ILogger<JugadoresSinGeneroController> _logger;

        public JugadoresSinGeneroController(IJugadoresSinGeneroDao dao, ILogger<JugadoresSinGeneroController> logger)
        {
            _dao = dao;
            _logger = logger;
        }

        // GET api/jugadores-sin-genero -> { total: 3, jugadores: [{ idJugador, nombreCompleto, dni, categoria }] } (total 0 si no falta nada)
        [HttpGet]
        public IActionResult Obtener()
        {
            try
            {
                var resultado = _dao.Obtener(MaximoListado);
                return Ok(new
                {
                    total = resultado.Total,
                    jugadores = resultado.Primeros.Select(j => new
                    {
                        idJugador = j.IdJugador,
                        nombreCompleto = j.NombreCompleto,
                        dni = j.Dni,
                        categoria = j.Categoria
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al listar los jugadores sin género");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al revisar los jugadores sin género." });
            }
        }
    }
}
