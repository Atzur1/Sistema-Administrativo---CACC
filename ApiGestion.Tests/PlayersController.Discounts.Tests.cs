namespace ApiGestion.Tests;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;

// Benefit rules of PlayersController that are resolved before reaching the
// database (HU-011). The DAOs get an empty connection string: if a test reached
// them it would fail, which proves each rule answers on its own.
//
// The rules that need the database (unknown player, invalid reason, duplicate
// benefit, edit and cancel) are covered by the integration script and the
// Postman collection, and are documented in QA-HU-011.md.
public class PlayersControllerDiscountsTests
{
    private static PlayersController CreateController()
    {
        return new PlayersController(
            NullLogger<PlayersController>.Instance,
            new PlayerDao(""),
            new DiscountDao(""));
    }

    private static DiscountRequestDTO ValidRequest()
    {
        return new DiscountRequestDTO
        {
            Reason = "Media Beca",
            ValueType = "%",
            Percentage = 50m,
            StartDate = "2026-09-15"
        };
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-577)]
    public void GetDiscountByPlayer_PlayerIdNotPositive_ReturnsBadRequest(long playerId)
    {
        IActionResult result = CreateController().GetDiscountByPlayer(playerId);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("greater than zero", badRequest.Value!.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AssignDiscount_PlayerIdNotPositive_ReturnsBadRequest(long playerId)
    {
        IActionResult result = CreateController().AssignDiscount(playerId, ValidRequest());

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("greater than zero", badRequest.Value!.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void UpdateDiscount_PlayerIdNotPositive_ReturnsBadRequest(long playerId)
    {
        IActionResult result = CreateController().UpdateDiscount(playerId, ValidRequest());

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("greater than zero", badRequest.Value!.ToString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void CancelDiscount_PlayerIdNotPositive_ReturnsBadRequest(long playerId)
    {
        IActionResult result = CreateController().CancelDiscount(playerId);

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("greater than zero", badRequest.Value!.ToString());
    }
}
