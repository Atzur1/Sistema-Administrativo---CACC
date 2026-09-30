using ApiGestion.Models;
using DaoLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiGestion.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "1")]
public sealed class AuditoriaController(IAuditDao auditDao) : ControllerBase
{
    [HttpGet("cambios")]
    public IActionResult BuscarCambios(
        [FromQuery] DateTime? desdeUtc = null,
        [FromQuery] DateTime? hastaUtc = null,
        [FromQuery] int? idUsuario = null,
        [FromQuery] string? emailUsuario = null,
        [FromQuery] string? entidad = null,
        [FromQuery] string? accion = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 50)
    {
        if (pagina < 1 || tamanoPagina is < 1 or > 100 || pagina > int.MaxValue / tamanoPagina || idUsuario is <= 0)
            return BadRequest(new { mensaje = "Los filtros de paginación o usuario no son válidos." });
        if (desdeUtc.HasValue && hastaUtc.HasValue && desdeUtc.Value >= hastaUtc.Value)
            return BadRequest(new { mensaje = "El intervalo UTC debe tener inicio anterior al fin." });
        if (entidad?.Length > 128 || emailUsuario?.Length > 254)
            return BadRequest(new { mensaje = "El nombre de entidad supera el largo permitido." });
        // Valores propios del filtro, no acciones crudas de SQL Server: cada uno ya
        // implica su sección (ver AuditDao.Search). Es a propósito que no incluya
        // CUOTA_SALDADA: sigue existiendo como etiqueta de fila, pero no es algo
        // que el club necesite buscar aparte.
        string? accionNormalizada = accion?.Trim().ToUpperInvariant();
        if (accionNormalizada is not null && !new[] { "PAGO_REALIZADO", "ARANCEL_ACTUALIZADO", "DELETE" }.Contains(accionNormalizada))
            return BadRequest(new { mensaje = "La acción debe ser PAGO_REALIZADO, ARANCEL_ACTUALIZADO o DELETE." });

        try
        {
            var (items, total) = auditDao.Search(
                desdeUtc, hastaUtc, idUsuario, emailUsuario, entidad, accionNormalizada, pagina, tamanoPagina);
            return Ok(new AuditSearchResponseDto
            {
                Page = pagina,
                PageSize = tamanoPagina,
                Total = total,
                Items = items
            });
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { mensaje = "No se pudo consultar la bitácora. Intente nuevamente." });
        }
    }
}
