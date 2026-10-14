namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using ServiceLibrary;

// Alta de jugadores (HU-033), búsqueda para Cuotas y Pagos (HU-023) y Becados y Descuentos (HU-011 / HU-012).
// Las reglas viven en ServiceLibrary (JugadoresAltaService y BeneficiosService): acá solo se valida la forma del
// pedido, se llama al servicio y se traduce el resultado a HTTP.
//
// Sin [Authorize] por rol a nivel de clase: se combinaría con AND contra el de cada acción y dejaría afuera al rol
// que no está en ambos (ver ReportesController). CreatePlayer y editar beneficios son solo del SuperAdmin; el resto
// de Becados y Descuentos también lo usa el rol 2.
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PlayersController : ControllerBase
{
    public const int MinSearchTermLength = 2;
    public const int MaxSearchTermLength = 50;
    public const int MaxSearchResults = 50;

    private readonly ILogger<PlayersController> _logger;
    private readonly IJugadoresAltaService _altaService;
    private readonly IBeneficiosService _beneficiosService;
    private readonly PlayerDAO _playerDAO;

    public PlayersController(
        ILogger<PlayersController> logger,
        IJugadoresAltaService altaService,
        IBeneficiosService beneficiosService,
        PlayerDAO playerDAO)
    {
        _logger = logger;
        _altaService = altaService;
        _beneficiosService = beneficiosService;
        _playerDAO = playerDAO;
    }

    // Registers a new player (HU-033) with the charges the club applies: the monthly fee from the 10th on and,
    // for the men's squad only, the enrollment fee in force (see JugadoresAltaService).
    [Authorize(Roles = "1")]
    [HttpPost]
    public IActionResult CreatePlayer(PlayerRequestDTO request)
    {
        var resultado = _altaService.Registrar(new SolicitudAltaJugador(
            request.FirstName, request.LastName, request.Dni, request.BirthDate!.Value, request.Gender, request.CategoryId!.Value));

        return this.Responder(resultado, alta =>
        {
            Player created = alta.Jugador;
            _logger.LogInformation("Player {PlayerId} registered (monthly fee: {MonthlyFee}, enrollment fee: {EnrollmentFee})",
                created.Id, alta.MontoCuota, alta.MontoInscripcion);

            return Created($"/api/players/{created.Id}", new PlayerResponseDTO
            {
                Id = created.Id,
                FirstName = created.FirstName,
                LastName = created.LastName,
                Dni = created.Dni,
                BirthDate = created.BirthDate.ToString("yyyy-MM-dd"),
                Gender = created.Gender,
                CategoryId = created.CategoryId,
                JoinDate = created.JoinDate.ToString("yyyy-MM-dd"),
                MonthlyFeeAmount = alta.MontoCuota,
                EnrollmentFeeAmount = alta.MontoInscripcion
            });
        });
    }

    // Player search on the Cuotas panel (HU-023): by DNI, first name or last name (several words, in
    // any order), so the operator can open that player's fees and debt. Both administrative roles
    // manage fees, as on the rest of PagosController.
    [Authorize(Roles = "1,2")]
    [HttpGet("search")]
    public IActionResult SearchPlayers([FromQuery] string? term)
    {
        string trimmed = term?.Trim() ?? "";

        if (trimmed.Length < MinSearchTermLength)
        {
            return BadRequest(new { mensaje = $"Ingresá al menos {MinSearchTermLength} caracteres del DNI, del nombre o del apellido." });
        }

        if (trimmed.Length > MaxSearchTermLength)
        {
            return BadRequest(new { mensaje = $"La búsqueda admite hasta {MaxSearchTermLength} caracteres." });
        }

        try
        {
            IReadOnlyList<PlayerSearchResult> players = _playerDAO.SearchPlayersForFeeManagement(trimmed, MaxSearchResults);
            _logger.LogInformation("Player search returned {Count} matches", players.Count);

            return Ok(players.Select(player => new PlayerSearchResultDTO
            {
                Id = player.Id,
                FirstName = player.FirstName,
                LastName = player.LastName,
                Dni = player.Dni,
                CategoryName = player.CategoryName,
                LeaveDate = player.LeaveDate?.ToString("yyyy-MM-dd")
            }).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching players");
            return StatusCode(500, new { mensaje = "No se pudo realizar la búsqueda de jugadores." });
        }
    }

    // Feeds the badge on the treasury grid: a single call returns every active
    // discount and the front end matches them by PlayerId.
    [Authorize(Roles = "1,2")]
    [HttpGet("discounts")]
    public IActionResult GetDiscounts() => Ok(_beneficiosService.ObtenerVigentes().Select(MapToDto).ToList());

    // Feeds the administration table on Becados y Descuentos, which lists current
    // and expired assignments alike
    [Authorize(Roles = "1,2")]
    [HttpGet("discounts/all")]
    public IActionResult GetAllDiscounts() => Ok(_beneficiosService.ObtenerTodos().Select(MapToDto).ToList());

    // Feeds the reason dropdown on the assignment form. The list comes from the
    // catalogue table so the front end and the API can never disagree on what a
    // valid reason is.
    [Authorize(Roles = "1,2")]
    [HttpGet("discounts/types")]
    public IActionResult GetDiscountTypes() => Ok(_beneficiosService.ObtenerMotivos());

    // Feeds the badge on the player profile header and the assignment form.
    //
    // By default it answers only a benefit actually in force, which is the
    // contract HU-014 established and its QA suite checks: an expired or
    // cancelled benefit reads as 404.
    //
    // includeExpired=true widens it to the benefit the form has to work with even
    // when it is outside its window, which is what the popup opens with: the one
    // that applies today, or failing that the next scheduled one, or the last
    // one that expired.
    [Authorize(Roles = "1,2")]
    [HttpGet("{playerId}/discount")]
    public IActionResult GetDiscountByPlayer(long playerId, bool includeExpired = false)
    {
        if (playerId <= 0)
        {
            return BadRequest(new { mensaje = "Indicá un jugador válido." });
        }

        Discount? discount = _beneficiosService.ObtenerAsignado(playerId);
        if (discount == null || (!includeExpired && !discount.IsActive))
        {
            return NotFound();
        }

        return Ok(MapToDto(discount));
    }

    // Every benefit of a player that was not cancelled, oldest first (HU-012): what already expired, what runs
    // today and what is scheduled. Each one carries its own state, resolved by the server.
    [Authorize(Roles = "1,2")]
    [HttpGet("{playerId}/discounts")]
    public IActionResult GetDiscountsByPlayer(long playerId)
    {
        if (playerId <= 0)
        {
            return BadRequest(new { mensaje = "Indicá un jugador válido." });
        }

        return Ok(_beneficiosService.ObtenerDelJugador(playerId).Select(MapToDto).ToList());
    }

    // Assigns a benefit to a player (HU-011 / HU-012). A player can hold several over time, never two on the same
    // day; the overlap is checked again inside the insert's transaction, so a race between two requests also fails.
    [Authorize(Roles = "1,2")]
    [HttpPost("{playerId}/discount")]
    public IActionResult AssignDiscount(long playerId, DiscountRequestDTO request)
    {
        return this.Responder(_beneficiosService.Asignar(playerId, Solicitud(request)), creado =>
        {
            _logger.LogInformation("Benefit {DiscountId} assigned to player {PlayerId}", creado.Id, playerId);
            return Created($"/api/players/{playerId}/discount", MapToDto(creado));
        });
    }

    // Edita una bonificación del jugador. Solo SuperAdmin (el rol 2 asigna, cancela y anula, pero no
    // edita), y solo lo que todavía no es historia: ver ServiceLibrary.ReglasEdicionBeneficio.
    [Authorize(Roles = "1")]
    [HttpPut("{playerId}/discount")]
    public IActionResult UpdateDiscount(long playerId, DiscountRequestDTO request, long discountId = 0)
    {
        return this.Responder(_beneficiosService.Editar(playerId, discountId, Solicitud(request)), editado =>
        {
            _logger.LogInformation("Benefit {DiscountId} updated for player {PlayerId}", editado.Id, playerId);
            return Ok(MapToDto(editado));
        });
    }

    // Cancels a benefit before its time. The row is kept as history and only flipped to inactive: PAGOS may still
    // point at it, and the administration table has to keep showing what was granted.
    // discountId is required: without it the API would have to guess which benefit to cancel.
    [Authorize(Roles = "1,2")]
    [HttpDelete("{playerId}/discount")]
    public IActionResult CancelDiscount(long playerId, long discountId)
    {
        if (discountId <= 0)
        {
            return BadRequest(new { mensaje = "Indicá una bonificación válida." });
        }

        return this.Responder(_beneficiosService.Cancelar(playerId, discountId), cancelado =>
        {
            _logger.LogInformation("Benefit {DiscountId} cancelled for player {PlayerId}", cancelado, playerId);
            return NoContent();
        });
    }

    // Anula un beneficio asignado por error: deja de contar para todas sus cuotas, como si nunca se hubiera
    // otorgado (lo ya cobrado se mantiene). Solo uno cancelado (ver BeneficiosService.Anular).
    // POST y no DELETE: anular es una acción sobre el beneficio, la fila no se borra.
    [Authorize(Roles = "1,2")]
    [HttpPost("{playerId}/discount/{discountId}/void")]
    public IActionResult VoidDiscount(long playerId, long discountId)
    {
        return this.Responder(_beneficiosService.Anular(playerId, discountId), anulado =>
        {
            _logger.LogInformation("Benefit {DiscountId} voided for player {PlayerId}", anulado, playerId);
            return NoContent();
        });
    }

    // The DTO already validated the shape (dates parse, value matches its type), so the dates are never null here.
    private static SolicitudBeneficio Solicitud(DiscountRequestDTO request) => new(
        request.Reason,
        request.ValueType,
        request.Percentage,
        request.FixedAmount,
        DiscountRequestDTO.ParseDate(request.StartDate)!.Value,
        DiscountRequestDTO.ParseDate(request.EndDate)!.Value);

    private static DiscountResponseDTO MapToDto(Discount discount)
    {
        return new DiscountResponseDTO
        {
            Id = discount.Id,
            PlayerId = discount.PlayerId,
            PlayerName = discount.PlayerName,
            Category = discount.Category,
            Type = discount.Type,
            ValueType = discount.ValueType,
            Percentage = discount.Percentage,
            FixedAmount = discount.FixedAmount,
            StartDate = discount.StartDate.ToString("yyyy-MM-dd"),
            EndDate = discount.EndDate.ToString("yyyy-MM-dd"),
            // The state the server resolved, and IsActive read off it. Both come
            // from the same value, so they cannot contradict each other.
            Status = discount.Status.ToString(),
            IsActive = discount.IsActive
        };
    }
}
