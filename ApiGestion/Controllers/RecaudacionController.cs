using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers
{
    // HU-018: panel de Recaudación Mensual. Controller propio y no un método más
    // en PagosController porque no mueve plata ni toca cuotas: solo lee y agrega.
    //
    // Rol 1 (SuperAdmin) únicamente, igual que el ítem del sidebar y el
    // superAdminGuard de la ruta en el frontend: así no se entra por la API a lo
    // que la pantalla no muestra.
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "1")]
    public class RecaudacionController : ControllerBase
    {
        private readonly IRecaudacionDao _recaudacionDao;
        private readonly ILogger<RecaudacionController> _logger;

        public RecaudacionController(IRecaudacionDao recaudacionDao, ILogger<RecaudacionController> logger)
        {
            _recaudacionDao = recaudacionDao;
            _logger = logger;
        }

        // Mismo tamaño por defecto y mismo tope que "Pagos registrados" (HU-088).
        public const int TamanioPaginaPorDefecto = 10;
        public const int TamanioPaginaMaximo = 100;

        // Los únicos órdenes que acepta la API. El texto de la URL se traduce a un enum acá y el
        // ORDER BY lo arma el DAO desde ese enum: el nombre que manda el cliente nunca llega al SQL.
        private static readonly Dictionary<string, OrdenPagosDelPeriodo> Ordenes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["fecha"] = OrdenPagosDelPeriodo.Fecha,
                ["monto"] = OrdenPagosDelPeriodo.Monto,
            };

        // GET api/recaudacion?anio=2026&mes=10
        // GET api/recaudacion?anio=2026&mes=10&idCategoria=5 -> acota a una categoría
        // 200: { total, efectivo, transferencia, pagos, pagosEfectivo, pagosTransferencia }
        // HU-107: tarjetas y donut, siempre del período completo. Las filas van por api/recaudacion/pagos.
        [HttpGet]
        public IActionResult ObtenerDelPeriodo(
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? idCategoria = null)
        {
            if (PeriodoInvalido(anio, mes) is string error)
            {
                return BadRequest(new { exito = false, mensaje = error });
            }

            try
            {
                RecaudacionPeriodo periodo = _recaudacionDao.ObtenerTotalesDelPeriodo(anio, mes, idCategoria);
                return Ok(RecaudacionResponseDTO.FromEntity(periodo));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al obtener la recaudación de {Mes:00}/{Anio}", mes, anio);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener la recaudación." });
            }
        }

        // GET api/recaudacion/pagos?anio=2026&mes=10&idCategoria=5&page=1&pageSize=10&sortBy=fecha&sortDirection=desc
        // HU-107: tabla "Pagos del período", filtrada, ordenada y paginada en la base.
        // 200: { items: [...], total, pagina, tamanioPagina, totalPaginas }
        // 400: período, página, tamaño, orden o dirección inválidos
        [HttpGet("pagos")]
        public IActionResult ObtenerPagosDelPeriodo(
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? idCategoria = null,
            [FromQuery] int page = 1, [FromQuery] int pageSize = TamanioPaginaPorDefecto,
            [FromQuery] string sortBy = "fecha", [FromQuery] string sortDirection = "desc")
        {
            if (PeriodoInvalido(anio, mes) is string error)
            {
                return BadRequest(new { exito = false, mensaje = error });
            }
            if (page < 1)
            {
                return BadRequest(new { exito = false, mensaje = "La página tiene que ser 1 o mayor." });
            }
            if (pageSize < 1 || pageSize > TamanioPaginaMaximo)
            {
                return BadRequest(new { exito = false, mensaje = $"El tamaño de página tiene que estar entre 1 y {TamanioPaginaMaximo}." });
            }
            if ((long)(page - 1) * pageSize > int.MaxValue)
            {
                return BadRequest(new { exito = false, mensaje = "La página solicitada excede el rango permitido." });
            }
            if (!Ordenes.TryGetValue(sortBy ?? "", out OrdenPagosDelPeriodo orden))
            {
                return BadRequest(new { exito = false, mensaje = "Orden inválido: debe ser fecha o monto." });
            }
            bool? descendente = sortDirection?.ToLowerInvariant() switch { "asc" => false, "desc" => true, _ => null };
            if (descendente == null)
            {
                return BadRequest(new { exito = false, mensaje = "Dirección inválida: debe ser asc o desc." });
            }

            try
            {
                PagosDelPeriodoPagina pagina = _recaudacionDao.ObtenerPagosDelPeriodo(new FiltroPagosDelPeriodo
                {
                    Anio = anio,
                    Mes = mes,
                    IdCategoria = idCategoria,
                    Pagina = page,
                    TamanioPagina = pageSize,
                    OrdenarPor = orden,
                    Descendente = descendente.Value
                });
                return Ok(PeriodPaymentsPageResponseDTO.FromEntity(pagina));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al obtener los pagos de {Mes:00}/{Anio}", mes, anio);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al obtener los pagos del período." });
            }
        }

        private static string? PeriodoInvalido(int anio, int mes)
        {
            if (mes < 1 || mes > 12)
            {
                return "El mes debe estar entre 1 y 12.";
            }

            // Cota amplia a propósito: no es validación de negocio, solo evita
            // que un año absurdo llegue a la consulta.
            if (anio < 2000 || anio > 2100)
            {
                return "El año no es válido.";
            }

            return null;
        }
    }
}
