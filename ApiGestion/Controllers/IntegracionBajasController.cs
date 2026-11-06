using ApiGestion.Models;
using ApiGestion.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    // Contrato de integración v1 con el Portal Deportivo: la baja de un jugador. El Deportivo graba la baja (fecha_baja) directo
    // en la base compartida y después avisa acá para que el Administrativo haga lo que hace siempre una baja (con la lógica de
    // DarDeBaja, ya probada): quitar las cuotas futuras sin pagos y cancelar las becas. La deuda anterior se mantiene. Si el aviso
    // no llega, la baja igual quedó hecha (el jugador ya no recibe cuotas nuevas ni puede ingresar): se repite desde el Deportivo.
    [ApiController]
    [Route("api/integracion/v1/jugadores")]
    [Produces("application/json")]
    [Authorize(Roles = RolesDelDeportivo.SuperAdminAdministrativo + "," + RolesDelDeportivo.Administrador)]
    [EnableRateLimiting(IntegrationErrors.RateLimitPolicy)]
    public class IntegracionBajasController : ControllerBase
    {
        private readonly IJugadoresBajaService _bajaService;
        private readonly ILogger<IntegracionBajasController> _logger;

        public IntegracionBajasController(IJugadoresBajaService bajaService, ILogger<IntegracionBajasController> logger)
        {
            _bajaService = bajaService;
            _logger = logger;
        }

        // POST api/integracion/v1/jugadores/{idJugador}/baja -> 200 con lo que se quitó | 400 id inválido | 404 no existe
        // | 422 baja con fecha inválida | 401 / 403 sin sesión o sin permiso. Se puede repetir sin efecto.
        [HttpPost("{idJugador:int}/baja")]
        [ProducesResponseType(typeof(BajaInformadaResponseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult Baja(int idJugador)
        {
            if (idJugador <= 0)
            {
                return IntegrationErrors.Respond(this, _logger, StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput,
                    "Indicá un jugador válido.", "idJugador");
            }

            ResultadoOperacion<BajaAplicada> resultado;
            try
            {
                resultado = _bajaService.AplicarBajaInformada(idJugador);
            }
            catch (Exception ex)
            {
                // Si falla, la baja del Deportivo no se deshace: se repite el aviso (es idempotente).
                return IntegrationErrors.FromException(this, _logger, ex);
            }
            return IntegrationErrors.FromResult(this, _logger, resultado, baja =>
            {
                _logger.LogInformation("Baja informada del jugador {IdJugador}: {Cuotas} cuota(s) futuras quitadas, {Becas} beca(s) canceladas",
                    idJugador, baja.CuotasFuturasQuitadas, baja.BeneficiosCancelados);
                return Ok(new BajaInformadaResponseDTO
                {
                    IdJugador = idJugador,
                    CuotasFuturasQuitadas = baja.CuotasFuturasQuitadas,
                    BeneficiosCancelados = baja.BeneficiosCancelados
                });
            });
        }
    }
}
