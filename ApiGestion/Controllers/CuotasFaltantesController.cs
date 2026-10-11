using ApiGestion.Models;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    // Aviso de cuotas faltantes del Resumen General (solo SuperAdmin). Nada se genera solo: el SuperAdmin ve el
    // detalle de cada mes y decide generarlo. Cada cuota creada queda en Auditoría a su nombre (trigger de PAGOS).
    [ApiController]
    [Route("api/cuotas-faltantes")]
    [Authorize(Roles = "1")]
    public class CuotasFaltantesController : ControllerBase
    {
        private readonly ICuotasFaltantesService _service;
        private readonly ILogger<CuotasFaltantesController> _logger;

        public CuotasFaltantesController(ICuotasFaltantesService service, ILogger<CuotasFaltantesController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // GET api/cuotas-faltantes -> [{ periodo: "2026-09-01", cantidad: 45, montoTotal: 4140000 }] (vacío si no falta nada)
        [HttpGet]
        public IActionResult ObtenerMeses()
        {
            return Ok(_service.ObtenerMesesConFaltantes().Select(m => new
            {
                periodo = m.Periodo.ToString("yyyy-MM-dd"),
                cantidad = m.Cantidad,
                montoTotal = m.MontoTotal
            }));
        }

        // GET api/cuotas-faltantes/2026/9 -> a quién se le generaría la cuota de septiembre y por cuánto
        [HttpGet("{anio:int}/{mes:int}")]
        public IActionResult ObtenerDetalle(int anio, int mes)
        {
            try
            {
                return Ok(_service.ObtenerFaltantesDelMes(anio, mes).Select(MissingFeeResponseDTO.FromEntity).ToList());
            }
            catch (CuotasFaltantesInvalidasException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
        }

        // POST api/cuotas-faltantes/2026/9/generar -> emite las cuotas que faltan de ese mes
        [HttpPost("{anio:int}/{mes:int}/generar")]
        public IActionResult Generar(int anio, int mes)
        {
            try
            {
                int creadas = _service.GenerarFaltantesDelMes(anio, mes);
                _logger.LogInformation("Cuotas faltantes de {Mes:00}/{Anio} generadas: {Creadas} (usuario {IdUsuario})",
                    mes, anio, creadas, User?.FindFirst("idUsuario")?.Value);

                string mensaje = creadas == 0
                    ? "No había cuotas para generar en ese mes: ya estaban todas."
                    : $"Se generaron {creadas} cuota{(creadas == 1 ? "" : "s")} de {CalendarioCuotas.NombreDelPeriodo(new DateTime(anio, mes, 1)).ToLowerInvariant()}.";
                return Ok(new { exito = true, mensaje, creadas });
            }
            catch (CuotasFaltantesInvalidasException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
        }
    }
}
