namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
    // SQL Server codes for a unique index violation. The filtered index
    // UX_JUGDESC_UNA_ACTIVA raises one of these when two assignments for the
    // same player race past the check below, and that is a conflict, not a 500.
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private readonly ILogger<PlayersController> _logger;
    private readonly PlayerDao _playerDao;
    private readonly DiscountDao _discountDao;

    public PlayersController(ILogger<PlayersController> logger, PlayerDao playerDao, DiscountDao discountDao)
    {
        _logger = logger;
        _playerDao = playerDao;
        _discountDao = discountDao;
    }

    // Feeds the player lookup on the treasury screens
    [HttpGet]
    public IActionResult GetPlayers()
    {
        List<Player> players = _playerDao.GetAllPlayers();
        _logger.LogInformation("Players returned: {Count}", players.Count);

        return Ok(players.Select(MapPlayerToDto).ToList());
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
    // includeExpired=true widens it to the open assignment even when it is out of
    // its date window. The benefit popup needs that: a player whose benefit
    // expired last month still holds the only active slot, so offering an empty
    // form there would promise an assignment the API is going to reject.
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

    // Assigns a benefit to a player (HU-011).
    //
    // The single active benefit rule is enforced here and not only in the form:
    // a direct call to the API hits the same check, and the filtered unique index
    // catches whatever slips through a race.
    [Authorize]
    [HttpPost("{playerId}/discount")]
    public IActionResult AssignDiscount(long playerId, DiscountRequestDTO request)
    {
        IActionResult? rejection = ValidatePlayerAndReason(playerId, request, out DiscountType? reason);
        if (rejection != null)
        {
            return rejection;
        }

        if (_discountDao.GetAssignedDiscountByPlayer(playerId) != null)
        {
            return Conflict($"Player {playerId} already has an active benefit. Edit or cancel it before assigning a new one.");
        }

        Discount discount = BuildDiscount(playerId, request, reason!);

        try
        {
            Discount created = _discountDao.CreateDiscount(discount);
            _logger.LogInformation("Benefit {DiscountId} assigned to player {PlayerId}", created.Id, playerId);

            // Read back the stored row: the insert alone does not know the player
            // name or the category, and the client has to receive the same shape
            // the GET returns.
            Discount? stored = _discountDao.GetAssignedDiscountByPlayer(playerId);

            return Created($"/api/players/{playerId}/discount", MapToDto(stored ?? created));
        }
        catch (SqlException exception) when (IsUniqueViolation(exception))
        {
            // Another request got there first between the check and the insert
            _logger.LogWarning("Concurrent benefit assignment rejected for player {PlayerId}", playerId);
            return Conflict($"Player {playerId} already has an active benefit. Edit or cancel it before assigning a new one.");
        }
    }

    // Edits the benefit a player already holds. Only the open assignment can be
    // touched: a cancelled one stays as it was granted.
    [Authorize]
    [HttpPut("{playerId}/discount")]
    public IActionResult UpdateDiscount(long playerId, DiscountRequestDTO request)
    {
        IActionResult? rejection = ValidatePlayerAndReason(playerId, request, out DiscountType? reason);
        if (rejection != null)
        {
            return rejection;
        }

        Discount? current = _discountDao.GetAssignedDiscountByPlayer(playerId);
        if (current == null)
        {
            return NotFound($"Player {playerId} has no active benefit to edit.");
        }

        Discount discount = BuildDiscount(playerId, request, reason!);
        discount.Id = current.Id;

        if (!_discountDao.UpdateDiscount(discount))
        {
            return NotFound($"Player {playerId} has no active benefit to edit.");
        }

        _logger.LogInformation("Benefit {DiscountId} updated for player {PlayerId}", discount.Id, playerId);

        // Read back what was stored: the client renders persisted data, never the
        // payload it just sent.
        Discount? stored = _discountDao.GetAssignedDiscountByPlayer(playerId);
        return Ok(MapToDto(stored ?? discount));
    }

    // Cancels the benefit of a player. The row is kept as history and only
    // flipped to inactive, which frees the player to receive a new one.
    [Authorize]
    [HttpDelete("{playerId}/discount")]
    public IActionResult CancelDiscount(long playerId)
    {
        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        if (!_discountDao.DeactivateDiscount(playerId))
        {
            return NotFound($"Player {playerId} has no active benefit to cancel.");
        }

        _logger.LogInformation("Benefit cancelled for player {PlayerId}", playerId);

        return NoContent();
    }

    // The two checks every write shares: the player has to exist and the reason
    // has to be one of the catalogue rows.
    private IActionResult? ValidatePlayerAndReason(long playerId, DiscountRequestDTO request, out DiscountType? reason)
    {
        reason = null;

        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        if (_playerDao.GetPlayerById(playerId) == null)
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
            StartDate = DiscountRequestDTO.ParseDate(request.StartDate) ?? DateTime.Today,
            EndDate = DiscountRequestDTO.ParseDate(request.EndDate),
            IsActive = true
        };
    }

    private static bool IsUniqueViolation(SqlException exception)
    {
        return exception.Number == UniqueIndexViolation || exception.Number == UniqueConstraintViolation;
    }

    private PlayerResponseDTO MapPlayerToDto(Player player)
    {
        return new PlayerResponseDTO
        {
            Id = player.Id,
            FullName = $"{player.LastName}, {player.FirstName}",
            Document = player.Document,
            Category = player.Category
        };
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
            StartDate = discount.StartDate == DateTime.MinValue ? "" : discount.StartDate.ToString("yyyy-MM-dd"),
            // Empty string, not "0001-01-01": a benefit with no end date has no
            // date to render and the client checks for the empty value.
            EndDate = discount.EndDate?.ToString("yyyy-MM-dd") ?? "",
            IsActive = discount.IsActive
        };
    }
}
