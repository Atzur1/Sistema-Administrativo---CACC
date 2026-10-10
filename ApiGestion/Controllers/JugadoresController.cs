using ApiGestion.Models;
using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ApiGestion.Controllers
{
    // Sin [Authorize] a nivel de clase: se combinaría con AND contra el de cada
    // acción y dejaría afuera al rol que no está en ambos (ver ReportesController).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class JugadoresController : ControllerBase
    {
        private readonly IJugadoresDao _jugadoresDao;
        private readonly IPagosDao _pagosDao;
        private readonly ILogger<JugadoresController> _logger;

        [ActivatorUtilitiesConstructor]
        public JugadoresController(IJugadoresDao jugadoresDao, IPagosDao pagosDao, ILogger<JugadoresController> logger)
        {
            _jugadoresDao = jugadoresDao;
            _pagosDao = pagosDao;
            _logger = logger;
        }

        public JugadoresController(IJugadoresDao jugadoresDao, IPagosDao pagosDao)
            : this(jugadoresDao, pagosDao, Microsoft.Extensions.Logging.Abstractions.NullLogger<JugadoresController>.Instance) { }

        // GET api/jugadores -> lista completa (nombre, dni, categoría). La usan el
        // buscador de Cuotas y Pagos y el de Becados y Descuentos (rol 1 y 2).
        [Authorize(Roles = "1,2")]
        [HttpGet]
        public IActionResult ListarJugadores()
        {
            try
            {
                return Ok(_jugadoresDao.ListarJugadores());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al obtener los jugadores");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener los jugadores." });
            }
        }

        // GET api/jugadores/{id} -> datos básicos para el header del perfil de jugador.
        // También lo usa Deuda de Jugador (rol 1 y 2), a la que se llega desde Deudas y Morosidad.
        [Authorize(Roles = "1,2")]
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
                _logger.LogError(ex, "Error interno al obtener el jugador {JugadorId}", id);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el jugador." });
            }
        }

        // POST api/jugadores/{id}/baja  Body (opcional): { "fecha": "2026-10-15" } -> por defecto, hoy.
        // El jugador deja de recibir cuotas desde el mes siguiente al de la baja. Solo SuperAdmin.
        [Authorize(Roles = "1")]
        [HttpPost("{id:int}/baja")]
        public IActionResult DarDeBaja(int id, [FromBody] BajaJugadorRequestDto? request)
        {
            DateTime hoy = EntityLibrary.RelojNegocio.Hoy;
            DateTime fecha = request?.Fecha?.Date ?? hoy;
            if (fecha > hoy)
            {
                return BadRequest(new { exito = false, mensaje = "La fecha de baja no puede ser futura." });
            }

            try
            {
                var (resultado, cuotasEliminadas) = _jugadoresDao.DarDeBaja(id, fecha);
                return resultado switch
                {
                    BajaJugadorResultado.NoEncontrado => NotFound(new { exito = false, mensaje = "Jugador no encontrado." }),
                    BajaJugadorResultado.YaDadoDeBaja => Conflict(new { exito = false, mensaje = "El jugador ya está dado de baja." }),
                    BajaJugadorResultado.FechaInvalida => BadRequest(new { exito = false, mensaje = "La fecha de baja no puede ser anterior a la de alta." }),
                    _ => Ok(new
                    {
                        exito = true,
                        cuotasEliminadas,
                        mensaje = cuotasEliminadas > 0
                            ? $"Jugador dado de baja. Se quitaron {cuotasEliminadas} cuota(s) sin pagos de meses posteriores a la baja."
                            : "Jugador dado de baja. No se le emitirán más cuotas."
                    })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al dar de baja al jugador {JugadorId}", id);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al dar de baja al jugador." });
            }
        }

        // POST api/jugadores/{id}/reactivar -> vuelve a emitirle cuotas desde el mes en curso. Solo SuperAdmin.
        [Authorize(Roles = "1")]
        [HttpPost("{id:int}/reactivar")]
        public IActionResult Reactivar(int id)
        {
            try
            {
                if (!_jugadoresDao.Reactivar(id))
                {
                    return BadRequest(new { exito = false, mensaje = "No se pudo reactivar: el jugador no existe o no estaba dado de baja." });
                }

                // La cuota del mes en curso se emite ahora, sin esperar al próximo turno del generador.
                DateTime hoy = EntityLibrary.RelojNegocio.Hoy;
                _pagosDao.GenerarCuotasPendientesDelMes(null, null, hoy.Month, hoy.Year);
                return Ok(new { exito = true, mensaje = "Jugador reactivado." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al reactivar al jugador {JugadorId}", id);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al reactivar al jugador." });
            }
        }

        // GET api/jugadores/{id}/historial-pagos?page=1&pageSize=10 -> historial paginado, más reciente primero
        [Authorize(Roles = "1")]
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
                _logger.LogError(ex, "Error interno al obtener el historial de pagos del jugador {JugadorId}", id);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener el historial de pagos." });
            }
        }
    }
}
