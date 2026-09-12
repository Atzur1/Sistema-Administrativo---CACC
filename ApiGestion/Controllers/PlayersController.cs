namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

[ApiController]
[Route("api/[controller]")]
public class PlayersController : ControllerBase
{
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

    // Feeds the badge on the player profile header
    [HttpGet("{playerId}/discount")]
    public IActionResult GetDiscountByPlayer(long playerId)
    {
        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        Discount? discount = _discountDao.GetActiveDiscountByPlayer(playerId);
        if (discount == null)
        {
            return NotFound();
        }

        return Ok(MapToDto(discount));
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
            Percentage = discount.Percentage,
            StartDate = discount.StartDate.ToString("yyyy-MM-dd"),
            EndDate = discount.EndDate.ToString("yyyy-MM-dd"),
            IsActive = discount.IsActive
        };
    }
}
