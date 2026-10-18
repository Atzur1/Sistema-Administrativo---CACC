using ApiGestion.Models;
using ApiGestion.Services;
using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    // Estado de deuda de los jugadores para el Portal Deportivo (QR de ingreso y listados). El Deportivo reenvía el token
    // del usuario: es el mismo en los dos portales. Ver RolesDelDeportivo para quién puede consultar y qué ve cada rol.
    [ApiController]
    [Route("api/estado-deuda")]
    [Authorize(Roles = RolesDelDeportivo.QuienConsulta)]
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
        public IActionResult PorDni(string dni)
        {
            string? normalizado = IdentificadorCuenta.NormalizarDni(dni);
            if (normalizado is null)
            {
                return BadRequest(new { exito = false, mensaje = "Indicá un DNI válido (solo números, de 6 a 9 dígitos)." });
            }

            try
            {
                var encontrados = _estadoDeudaDao.ObtenerPorDni(new[] { normalizado }, CategoriasPermitidas());
                if (encontrados.Count == 0)
                {
                    return NotFound(new { exito = false, mensaje = "El DNI no pertenece a un jugador." });
                }
                if (encontrados.Count > 1)
                {
                    return Conflict(new { exito = false, mensaje = "El DNI está asociado a más de un jugador." });
                }
                return Ok(EstadoDeudaResponseDTO.FromEntity(encontrados[0]));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al consultar el estado de deuda");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al consultar el estado de deuda." });
            }
        }

        // POST api/estado-deuda/lote  { "dnis": ["41000001", "42.000.002"] } -> el estado de cada uno, para los listados.
        [HttpPost("lote")]
        public IActionResult PorLote([FromBody] EstadoDeudaLoteRequestDTO request)
        {
            if (request?.Dnis is null || request.Dnis.Count == 0)
            {
                return BadRequest(new { exito = false, mensaje = "Indicá al menos un DNI." });
            }
            if (request.Dnis.Count > MaximoPorLote)
            {
                return BadRequest(new { exito = false, mensaje = $"Se pueden consultar hasta {MaximoPorLote} DNI por vez." });
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
                _logger.LogError(ex, "Error al consultar el estado de deuda por lote");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al consultar el estado de deuda." });
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
