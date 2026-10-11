using ApiGestion.Models;
using DaoLibrary;
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

        public RecaudacionController(IRecaudacionDao recaudacionDao)
        {
            _recaudacionDao = recaudacionDao;
        }

        // GET api/recaudacion?anio=2026&mes=10
        // GET api/recaudacion?anio=2026&mes=10&idCategoria=5 -> acota a una categoría
        // 200: { total, efectivo, transferencia, pagos, pagosEfectivo, pagosTransferencia, detalle: [...] }
        [HttpGet]
        public IActionResult ObtenerDelPeriodo(
            [FromQuery] int anio, [FromQuery] int mes, [FromQuery] int? idCategoria = null)
        {
            if (mes < 1 || mes > 12)
            {
                return BadRequest(new { exito = false, mensaje = "El mes debe estar entre 1 y 12." });
            }

            // Cota amplia a propósito: no es validación de negocio, solo evita
            // que un año absurdo llegue a la consulta.
            if (anio < 2000 || anio > 2100)
            {
                return BadRequest(new { exito = false, mensaje = "El año no es válido." });
            }

            var periodo = _recaudacionDao.ObtenerDelPeriodo(anio, mes, idCategoria);

            return Ok(new RecaudacionResponseDTO
            {
                Total = periodo.Total,
                Efectivo = periodo.Efectivo,
                Transferencia = periodo.Transferencia,
                Pagos = periodo.Pagos,
                PagosEfectivo = periodo.PagosEfectivo,
                PagosTransferencia = periodo.PagosTransferencia,
                Detalle = periodo.Detalle.Select(p => new PagoDelPeriodoDTO
                {
                    IdPago = p.IdPago,
                    Jugador = p.Jugador,
                    Dni = p.Dni,
                    Categoria = p.Categoria,
                    Monto = p.Monto,
                    MedioPago = p.MedioPago,
                }).ToList(),
            });
        }
    }
}
