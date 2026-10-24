using ApiGestion.Models;
using ApiGestion.Services;
using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApiGestion.Controllers
{
    // Estado de deuda de los jugadores para el Portal Deportivo (QR de ingreso y listados). El Deportivo reenvía el token
    // del usuario: es el mismo en los dos portales. Ver RolesDelDeportivo para quién puede consultar y qué ve cada rol.
    [ApiController]
    [Route("api/integracion/v1/estado-deuda")]
    // Ruta anterior, sin versión: se mantiene solo mientras el Portal Deportivo migra a la v1 y se retira después.
    [Route("api/estado-deuda")]
    [Produces("application/json")]
    [Authorize(Roles = RolesDelDeportivo.QuienConsulta)]
    // HU-058: un pico de consultas del Deportivo no puede dejar sin conexiones a la base a las pantallas de cobranzas.
    [EnableRateLimiting(IntegrationErrors.RateLimitPolicy)]
    public class EstadoDeudaController : ControllerBase
    {
        public const int MaximoPorLote = 500;

        private readonly IEstadoDeudaDao _estadoDeudaDao;
        private readonly ILogger<EstadoDeudaController> _logger;

        public EstadoDeudaController(IEstadoDeudaDao estadoDeudaDao, ILogger<EstadoDeudaController> logger)
        {
            _estadoDeudaDao = estadoDeudaDao;
            _logger = logger;
        }

        // GET api/estado-deuda/dni/41000001 -> 200 con el estado | 400 DNI inválido | 404 no es un jugador (o no es de tu
        // alcance) | 409 el DNI corresponde a más de un jugador.
        [HttpGet("dni/{dni}")]
        [ProducesResponseType(typeof(EstadoDeudaResponseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status404NotFound)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status409Conflict)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult PorDni(string dni)
        {
            string? normalizado = IdentificadorCuenta.NormalizarDni(dni);
            if (normalizado is null)
            {
                return IntegrationErrors.Respond(this, _logger, StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput,
                    "Indicá un DNI válido (solo números, de 6 a 9 dígitos).", "dni");
            }

            try
            {
                var encontrados = _estadoDeudaDao.ObtenerPorDni(new[] { normalizado }, CategoriasPermitidas());
                if (encontrados.Count == 0)
                {
                    return IntegrationErrors.Respond(this, _logger, StatusCodes.Status404NotFound, IntegrationErrorCodes.NotFound,
                        "El DNI no pertenece a un jugador.");
                }
                if (encontrados.Count > 1)
                {
                    return IntegrationErrors.Respond(this, _logger, StatusCodes.Status409Conflict, IntegrationErrorCodes.Conflict,
                        "El DNI está asociado a más de un jugador.");
                }
                return Ok(EstadoDeudaResponseDTO.FromEntity(encontrados[0]));
            }
            catch (Exception ex)
            {
                return IntegrationErrors.FromException(this, _logger, ex);
            }
        }

        // POST api/estado-deuda/lote  { "dnis": ["41000001", "42.000.002"] } -> el estado de cada uno, para los listados.
        [HttpPost("lote")]
        [ProducesResponseType(typeof(EstadoDeudaLoteResponseDTO), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(IntegrationErrorResponseDTO), StatusCodes.Status503ServiceUnavailable)]
        public IActionResult PorLote([FromBody] EstadoDeudaLoteRequestDTO request)
        {
            if (request?.Dnis is null || request.Dnis.Count == 0)
            {
                return IntegrationErrors.Respond(this, _logger, StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput,
                    "Indicá al menos un DNI.", "dnis");
            }
            if (request.Dnis.Count > MaximoPorLote)
            {
                return IntegrationErrors.Respond(this, _logger, StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput,
                    $"Se pueden consultar hasta {MaximoPorLote} DNI por vez.", "dnis");
            }

            var respuesta = new EstadoDeudaLoteResponseDTO();
            var validos = new List<string>();
            foreach (string original in request.Dnis)
            {
                string? normalizado = IdentificadorCuenta.NormalizarDni(original);
                if (normalizado is null) respuesta.NoEncontrados.Add(original);
                else if (!validos.Contains(normalizado)) validos.Add(normalizado);
            }

            try
            {
                var encontrados = _estadoDeudaDao.ObtenerPorDni(validos, CategoriasPermitidas());
                respuesta.Items.AddRange(encontrados.Select(EstadoDeudaResponseDTO.FromEntity));
                var dnisEncontrados = encontrados.Select(e => e.Dni).ToHashSet();
                respuesta.NoEncontrados.AddRange(validos.Where(dni => !dnisEncontrados.Contains(dni)));
                return Ok(respuesta);
            }
            catch (Exception ex)
            {
                return IntegrationErrors.FromException(this, _logger, ex);
            }
        }

        // null = ve a cualquier jugador. El Director Técnico y el Preparador Físico ven solo las categorías de su token
        // (claims "id_categoria"); sin categorías asignadas no ven a nadie.
        private IReadOnlyCollection<int>? CategoriasPermitidas()
        {
            if (RolesDelDeportivo.VenATodos.Any(User.IsInRole)) return null;
            return User.FindAll("id_categoria")
                .Select(c => int.TryParse(c.Value, out int id) ? id : 0)
                .Where(id => id > 0)
                .Distinct()
                .ToList();
        }
    }
}
