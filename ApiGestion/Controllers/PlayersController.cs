namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// Becados y Descuentos (HU-011 / HU-012). Traído del repo de Atzur1, adaptado
// para reusar el IJugadoresDao que ya existe en este proyecto en vez de crear
// un PlayerDao/PlayersController.GetPlayers paralelo — evita duplicar el
// concepto de "jugador" que ya resuelve JugadoresController/JugadoresDao.
[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly ILogger<PlayersController> _logger;
    private readonly IJugadoresDao _jugadoresDao;
    private readonly DiscountDao _discountDao;

    public PlayersController(ILogger<PlayersController> logger, IJugadoresDao jugadoresDao, DiscountDao discountDao)
    {
        _logger = logger;
        _jugadoresDao = jugadoresDao;
        _discountDao = discountDao;
    }

    // Feeds the badge on the treasury grid: a single call returns every active
    // discount and the front end matches them by PlayerId.
    [HttpGet("discounts")]
    public IActionResult GetDiscounts()
    {
        List<Discount> discounts = _discountDao.GetActiveDiscounts();
        _logger.LogInformation("Active discounts found: {Count}", discounts.Count);

        return Ok(discounts.Select(MapToDto).ToList());
    }

    // Feeds the administration table on Becados y Descuentos, which lists current
    // and expired assignments alike
    [HttpGet("discounts/all")]
    public IActionResult GetAllDiscounts()
    {
        List<Discount> discounts = _discountDao.GetAllDiscounts();
        _logger.LogInformation("Discount assignments returned: {Count}", discounts.Count);

        return Ok(discounts.Select(MapToDto).ToList());
    }

    // Feeds the reason dropdown on the assignment form. The list comes from the
    // catalogue table so the front end and the API can never disagree on what a
    // valid reason is.
    [HttpGet("discounts/types")]
    public IActionResult GetDiscountTypes()
    {
        List<DiscountType> types = _discountDao.GetDiscountTypes();
        _logger.LogInformation("Discount types returned: {Count}", types.Count);

        return Ok(types.Select(type => type.Name).ToList());
    }

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
    [HttpGet("{playerId}/discount")]
    public IActionResult GetDiscountByPlayer(long playerId, bool includeExpired = false)
    {
        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        Discount? discount = _discountDao.GetAssignedDiscountByPlayer(playerId);
        if (discount == null || (!includeExpired && !discount.IsActive))
        {
            return NotFound();
        }

        return Ok(MapToDto(discount));
    }

    // Every benefit of a player that was not cancelled, oldest first (HU-012).
    //
    // Since a player can hold several over time, the financial card lists them
    // all: what already expired, what runs today and what is scheduled. Each one
    // carries its own state, resolved by the server.
    [HttpGet("{playerId}/discounts")]
    public IActionResult GetDiscountsByPlayer(long playerId)
    {
        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        List<Discount> discounts = _discountDao.GetDiscountsByPlayer(playerId);
        _logger.LogInformation("Benefits returned for player {PlayerId}: {Count}", playerId, discounts.Count);

        return Ok(discounts.Select(MapToDto).ToList());
    }

    // Assigns a benefit to a player (HU-011 / HU-012).
    //
    // Since HU-012 a player can hold several benefits over time, so what is
    // refused is not a second benefit but a range that overlaps another one of
    // the same player: on any given date, only one benefit can apply. The check
    // lives in the DAO, inside the transaction that inserts, so a direct call to
    // the API and a race between two requests hit the same rule.
    [Authorize]
    [HttpPost("{playerId}/discount")]
    public IActionResult AssignDiscount(long playerId, DiscountRequestDTO request)
    {
        IActionResult? rejection = ValidatePlayerAndReason(playerId, request, out DiscountType? reason);
        if (rejection != null)
        {
            return rejection;
        }

        Discount discount = BuildDiscount(playerId, request, reason!);

        // Asked before writing only to name the offending benefit in the error;
        // the write path checks again while holding the range.
        Discount? clash = _discountDao.GetOverlappingDiscount(playerId, discount.StartDate, discount.EndDate);
        if (clash != null)
        {
            return Conflict(OverlapMessage(playerId, clash));
        }

        Discount? created = _discountDao.CreateDiscount(discount);
        if (created == null)
        {
            // Another request took the range between the check and the insert
            _logger.LogWarning("Concurrent benefit assignment rejected for player {PlayerId}", playerId);
            return Conflict($"Player {playerId} already has a benefit covering that period. Adjust the dates or cancel the existing one.");
        }

        _logger.LogInformation("Benefit {DiscountId} assigned to player {PlayerId}", created.Id, playerId);

        // Read back the stored row: the insert alone does not know the player
        // name, the category or the resolved state, and the client has to
        // receive the same shape the GET returns.
        Discount? stored = FindDiscount(playerId, created.Id);

        return Created($"/api/players/{playerId}/discount", MapToDto(stored ?? created));
    }

    // Edits a benefit the player already holds. Only a benefit that was not
    // cancelled can be touched: a cancelled one stays as it was granted.
    [Authorize]
    [HttpPut("{playerId}/discount")]
    public IActionResult UpdateDiscount(long playerId, DiscountRequestDTO request, long discountId = 0)
    {
        IActionResult? rejection = ValidatePlayerAndReason(playerId, request, out DiscountType? reason);
        if (rejection != null)
        {
            return rejection;
        }

        // Without an explicit id the one the popup is showing is edited
        Discount? current = discountId > 0
            ? FindDiscount(playerId, discountId)
            : _discountDao.GetAssignedDiscountByPlayer(playerId);

        if (current == null)
        {
            return NotFound($"Player {playerId} has no such benefit to edit.");
        }

        Discount discount = BuildDiscount(playerId, request, reason!);
        discount.Id = current.Id;

        Discount? clash = _discountDao.GetOverlappingDiscount(playerId, discount.StartDate, discount.EndDate, current.Id);
        if (clash != null)
        {
            return Conflict(OverlapMessage(playerId, clash));
        }

        bool? updated = _discountDao.UpdateDiscount(discount);

        if (updated == null)
        {
            _logger.LogWarning("Concurrent benefit edit rejected for player {PlayerId}", playerId);
            return Conflict($"Player {playerId} already has a benefit covering that period. Adjust the dates or cancel the existing one.");
        }

        if (updated == false)
        {
            return NotFound($"Player {playerId} has no such benefit to edit.");
        }

        _logger.LogInformation("Benefit {DiscountId} updated for player {PlayerId}", discount.Id, playerId);

        // Read back what was stored: the client renders persisted data, never the
        // payload it just sent.
        Discount? stored = FindDiscount(playerId, current.Id);
        return Ok(MapToDto(stored ?? discount));
    }

    // Cancels a benefit of a player. The row is kept as history and only flipped
    // to inactive: PAGOS may still point at it, and the administration table has
    // to keep showing what was granted.
    //
    // Cancelling is for taking a benefit down before its time. A benefit that
    // simply ran its course does not need this: it expires on its own the day
    // after its end date, and frees the period for a new one.
    [Authorize]
    [HttpDelete("{playerId}/discount")]
    public IActionResult CancelDiscount(long playerId, long discountId = 0)
    {
        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        // Without an explicit id the one the popup is showing is cancelled
        long target = discountId;
        if (target <= 0)
        {
            Discount? current = _discountDao.GetAssignedDiscountByPlayer(playerId);
            if (current == null)
            {
                return NotFound($"Player {playerId} has no benefit to cancel.");
            }
            target = current.Id;
        }

        if (!_discountDao.DeactivateDiscount(playerId, target))
        {
            return NotFound($"Player {playerId} has no such benefit to cancel.");
        }

        _logger.LogInformation("Benefit {DiscountId} cancelled for player {PlayerId}", target, playerId);

        return NoContent();
    }

    // The two checks every write shares: the player has to exist and the reason
    // has to be one of the catalogue rows.
    //
    // Adaptado para usar IJugadoresDao.ObtenerJugadorPorId(int) en vez del
    // PlayerDao del repo original, así este feature no duplica el stack de
    // Jugadores que ya existe en este proyecto.
    private IActionResult? ValidatePlayerAndReason(long playerId, DiscountRequestDTO request, out DiscountType? reason)
    {
        reason = null;

        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        if (_jugadoresDao.ObtenerJugadorPorId((int)playerId) == null)
        {
            return BadRequest($"No player exists with id {playerId}.");
        }

        reason = _discountDao.GetDiscountTypeByName(request.Reason.Trim());
        if (reason == null)
        {
            List<string> allowed = _discountDao.GetDiscountTypes().Select(type => type.Name).ToList();
            return BadRequest($"The benefit reason must be one of: {string.Join(", ", allowed)}.");
        }

        return null;
    }

    // Maps the request onto the entity, keeping percentage and fixed amount
    // exclusive: whichever does not match the value type is stored as null.
    private static Discount BuildDiscount(long playerId, DiscountRequestDTO request, DiscountType reason)
    {
        bool isPercentage = request.ValueType.Trim() == DiscountRequestDTO.PercentageValue;

        return new Discount
        {
            PlayerId = playerId,
            TypeId = reason.Id,
            Type = reason.Name,
            ValueType = request.ValueType.Trim(),
            Percentage = isPercentage ? request.Percentage : null,
            FixedAmount = isPercentage ? null : request.FixedAmount,
            // Both dates are mandatory and already validated by the DTO, so by
            // the time this runs they parse. The state is not set here: the
            // database resolves it when the row is read back.
            StartDate = DiscountRequestDTO.ParseDate(request.StartDate)!.Value,
            EndDate = DiscountRequestDTO.ParseDate(request.EndDate)!.Value
        };
    }

    // One specific benefit of a player, by id. Cancelled ones are not reachable
    // here, the same way they are not reachable anywhere else.
    private Discount? FindDiscount(long playerId, long discountId)
    {
        return _discountDao.GetDiscountsByPlayer(playerId)
            .FirstOrDefault(benefit => benefit.Id == discountId);
    }

    // Names the benefit standing in the way, with its period, so the message
    // tells the administrator what to move instead of just saying "no".
    private static string OverlapMessage(long playerId, Discount clash)
    {
        return $"Player {playerId} already has a benefit for that period: "
             + $"{clash.Type} from {clash.StartDate:yyyy-MM-dd} to {clash.EndDate:yyyy-MM-dd}. "
             + "Adjust the dates or cancel that one first.";
    }

    private DiscountResponseDTO MapToDto(Discount discount)
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
