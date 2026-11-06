using System.Diagnostics;
using ApiGestion.Models;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers
{
    // Sin [Authorize] a nivel de clase: se combinaría con AND contra el de cada
    // acción y dejaría afuera al rol que no está en ambos (ver ReportesController).
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ArancelesController : ControllerBase
    {
        private readonly IArancelesService _arancelesService;
        private readonly ILogger<ArancelesController> _logger;

        public ArancelesController(IArancelesService arancelesService, ILogger<ArancelesController> logger)
        {
            _arancelesService = arancelesService;
            _logger = logger;
        }

        // GET api/aranceles/historial -> tabla "Historial y aranceles programados"
        [Authorize(Roles = "1")]
        [HttpGet("historial")]
        public IActionResult ObtenerHistorial()
        {
            return Ok(_arancelesService.ObtenerHistorial().Select(TariffHistoryItemResponseDTO.FromEntity).ToList());
        }

        // GET api/aranceles/resumen -> header (arancel masculino/femenino vigente, próximo cambio).
        // También la usa "Cuotas y Pagos" (rol 2) para mostrar el monto de cuota vigente.
        [Authorize(Roles = "1,2")]
        [HttpGet("resumen")]
        public IActionResult ObtenerResumen()
        {
            return Ok(TariffSummaryResponseDTO.FromEntity(_arancelesService.ObtenerResumen()));
        }

        // POST api/aranceles/simular -> paso "Revisar impacto" del form (HU-106). No graba nada.
        // Body: { "genero": "Masculino", "tipo": "porcentaje", "valor": 10, "vigenteDesde": "2026-11-01" }
        //    o: { "idCategoria": 5, "tipo": "absoluto", "valor": 50000, "vigenteDesde": "2026-11-01" }
        [Authorize(Roles = "1")]
        [HttpPost("simular")]
        public IActionResult SimularArancel([FromBody] TariffChangeRequestDTO request)
        {
            if (!TariffChangeRequestDTO.Tipos.TryGetValue(request.Tipo ?? string.Empty, out TipoCambioArancel tipo))
            {
                return BadRequest(new { exito = false, mensaje = "Tipo de cambio inválido: usá absoluto, porcentaje o montoFijo." });
            }

            var reloj = Stopwatch.StartNew();
            try
            {
                ImpactoArancel impacto = _arancelesService.SimularArancel(Cambio(request, tipo));
                reloj.Stop();
                _logger.LogInformation(
                    "Simulación de arancel {Destino} ({Tipo} {Valor}): {Jugadores} jugadores en {Categorias} categorías, diferencia mensual {Diferencia}, en {Milisegundos} ms",
                    request.Genero ?? $"categoría {request.IdCategoria}", tipo, request.Valor, impacto.Jugadores, impacto.Categorias,
                    impacto.DiferenciaMensual, reloj.ElapsedMilliseconds);
                return Ok(TariffImpactResponseDTO.FromEntity(impacto));
            }
            catch (ArancelInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al simular el arancel");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al calcular el impacto del arancel." });
            }
        }

        // POST api/aranceles/programar -> "Confirmar actualización" del resumen de impacto (HU-106)
        // Body: el de simular + { "huella": "<la de la simulación>", "jugadoresConfirmados": 42 }
        // 409 con { impacto } si el impacto ya no es el que se revisó: no se grabó nada.
        [Authorize(Roles = "1")]
        [HttpPost("programar")]
        public IActionResult ProgramarArancel([FromBody] ScheduleTariffRequestDTO request)
        {
            if (!TariffChangeRequestDTO.Tipos.TryGetValue(request.Tipo ?? string.Empty, out TipoCambioArancel tipo))
            {
                return BadRequest(new { exito = false, mensaje = "Tipo de cambio inválido: usá absoluto, porcentaje o montoFijo." });
            }

            try
            {
                CambioArancel cambio = Cambio(request, tipo);
                ProgramarArancelResultado resultado = _arancelesService.ProgramarArancel(new ProgramarArancelRequest
                {
                    Genero = cambio.Genero,
                    IdCategoria = cambio.IdCategoria,
                    Tipo = cambio.Tipo,
                    Valor = cambio.Valor,
                    VigenteDesde = cambio.VigenteDesde,
                    Huella = request.Huella ?? string.Empty,
                    JugadoresConfirmados = request.JugadoresConfirmados
                });

                string mensaje = "Arancel programado correctamente.";
                if (resultado.Impacto is { } impacto)
                {
                    _logger.LogInformation(
                        "Arancel {Destino} programado ({Tipo} {Valor}, {MontoAnterior} -> {MontoNuevo}): {Jugadores} jugadores, diferencia mensual {Diferencia}",
                        request.Genero ?? $"categoría {request.IdCategoria}", tipo, request.Valor, impacto.MontoBase, impacto.MontoNuevo,
                        impacto.Jugadores, impacto.DiferenciaMensual);
                }
                if (resultado.SeEmiteElDia is { } dia)
                {
                    mensaje += $" Las cuotas con este monto se generan el {FormatoVisible.Fecha(dia)}.";
                }
                if (resultado.CuotasReemitidas > 0)
                {
                    mensaje += $" Se volvieron a emitir {resultado.CuotasReemitidas} cuota{(resultado.CuotasReemitidas == 1 ? "" : "s")} pendiente{(resultado.CuotasReemitidas == 1 ? "" : "s")} del mes en curso con el monto nuevo.";
                }
                if (resultado.CuotasConPagosConservadas > 0)
                {
                    mensaje += $" {resultado.CuotasConPagosConservadas} cuota{(resultado.CuotasConPagosConservadas == 1 ? "" : "s")} del mes ya tenía{(resultado.CuotasConPagosConservadas == 1 ? "" : "n")} pagos y se mantuvo{(resultado.CuotasConPagosConservadas == 1 ? "" : "ieron")} sin cambios.";
                }

                return Ok(new
                {
                    exito = true,
                    mensaje,
                    impacto = resultado.Impacto is null ? null : TariffImpactResponseDTO.FromEntity(resultado.Impacto)
                });
            }
            catch (ImpactoDesactualizadoException ex)
            {
                _logger.LogWarning("Confirmación de arancel rechazada: el impacto cambió desde la simulación ({Jugadores} jugadores ahora)",
                    ex.ImpactoActual.Jugadores);
                return Conflict(new { exito = false, mensaje = ex.Message, impacto = TariffImpactResponseDTO.FromEntity(ex.ImpactoActual) });
            }
            catch (ArancelInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al programar el arancel");
                return StatusCode(500, new { exito = false, mensaje = "Error interno al programar el arancel." });
            }
        }

        // DELETE api/aranceles/{id} -> botón "Cancelar" del historial. Quita el arancel y las cuotas
        // pendientes sin pagos emitidas con él; si alguna ya tiene pagos, se rechaza (400). Responde 204 sin cuerpo.
        [Authorize(Roles = "1")]
        [HttpDelete("{idArancel:int}")]
        public IActionResult CancelarArancel(int idArancel)
        {
            try
            {
                CancelarArancelResultado resultado = _arancelesService.CancelarArancel(idArancel);
                _logger.LogInformation("Arancel {IdArancel} cancelado ({CuotasEliminadas} cuotas pendientes eliminadas)",
                    idArancel, resultado.CuotasEliminadas);
                return NoContent();
            }
            catch (ArancelInvalidoException ex)
            {
                return BadRequest(new { exito = false, mensaje = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error interno al cancelar el arancel {IdArancel}", idArancel);
                return StatusCode(500, new { exito = false, mensaje = "Error interno al cancelar el arancel." });
            }
        }

        private static CambioArancel Cambio(TariffChangeRequestDTO request, TipoCambioArancel tipo) => new()
        {
            Genero = request.Genero,
            IdCategoria = request.IdCategoria,
            Tipo = tipo,
            Valor = request.Valor,
            VigenteDesde = request.VigenteDesde
        };
    }
}
