using ApiGestion.Models;
using ApiGestion.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using EntityLibrary;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    // Contrato de integración v1 con el Portal Deportivo. Cuando el Deportivo da de alta a un jugador, lo graba directo en la
    // base compartida; después avisa acá para que el Administrativo le genere la inscripción y la cuota del mes con las reglas
    // de siempre (las de JugadoresAltaService), sin duplicarlas en el otro sistema. Si el aviso no llega, el alta igual quedó
    // hecha: la cuota sale con la próxima generación mensual y la inscripción al registrar el primer pago.
    [ApiController]
    [Route("api/integracion/v1/jugadores")]
    [Produces("application/json")]
    [Authorize(Roles = RolesDelDeportivo.SuperAdminAdministrativo + "," + RolesDelDeportivo.Administrador)]
    [EnableRateLimiting(IntegrationErrors.RateLimitPolicy)]
    public class IntegracionJugadoresController : ControllerBase
    {
        private readonly IJugadoresAltaService _altaService;
        private readonly ILogger<IntegracionJugadoresController> _logger;

        public IntegracionJugadoresController(IJugadoresAltaService altaService, ILogger<IntegracionJugadoresController> logger)
        {
            _altaService = altaService;
            _logger = logger;
        }

        // POST api/integracion/v1/jugadores/{idJugador}/cargos-alta -> 200 con lo que se generó | 400 id inválido | 404 no existe
        // | 422 dado de baja o alta que no es reciente | 401 / 403 sin sesión o sin permiso. Se puede repetir sin duplicar nada.
        [HttpPost("{idJugador:int}/cargos-alta")]
        [ProducesResponseType(typeof(CargosAltaResponseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult CargosDeAlta(int idJugador)
        {
            if (idJugador <= 0)
            {
                return IntegrationErrors.Respond(this, _logger, StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput,
                    "Indicá un jugador válido.", "idJugador");
            }

            ResultadoOperacion<CargosGenerados> resultado;
            try
            {
                resultado = _altaService.GenerarCargosDeAlta(idJugador);
            }
            catch (Exception ex)
            {
                // Si falla, el alta del Deportivo no se deshace: los cargos salen con la generación automática (ver el README).
                return IntegrationErrors.FromException(this, _logger, ex);
            }
            return IntegrationErrors.FromResult(this, _logger, resultado, cargos =>
            {
                _logger.LogInformation("Cargos de alta del jugador {IdJugador}: inscripción {Inscripcion}, cuota {Cuota}",
                    idJugador, cargos.Inscripcion, cargos.Cuota);
                return Ok(new CargosAltaResponseDTO
                {
                    IdJugador = idJugador,
                    InscripcionGenerada = cargos.Inscripcion,
                    CuotaGenerada = cargos.Cuota
                });
            });
        }
    }
}
