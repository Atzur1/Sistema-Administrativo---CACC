namespace ApiGestion.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;

// Business rules of PaymentsController that are resolved before reaching the
// database (HU-015 / HU-016). The DAOs get an empty connection string: if a test
// reached them it would fail, which proves each rule answers on its own.
// Rules that need the database (unknown player, already paid period, successful
// payment) are covered by the Postman collection.
public class PaymentsControllerTests
{
    private static PaymentsController CreateController(ClaimsPrincipal? user = null)
    {
        PaymentsController controller = new PaymentsController(
            NullLogger<PaymentsController>.Instance,
            new PaymentDao(""),
            new PlayerDao(""));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user ?? new ClaimsPrincipal(new ClaimsIdentity()) }
        };

        return controller;
    }

    private static ClaimsPrincipal UserWithClaims(params Claim[] claims)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static PaymentRequestDTO Request(string method, string? reference = null)
    {
        return new PaymentRequestDTO
        {
            PlayerId = 577,
            PeriodYear = 2026,
            PeriodMonth = 7,
            Amount = 85000,
            Method = method,
            Reference = reference
        };
    }

    [Theory]
    [InlineData("cheque")]
    [InlineData("tarjeta")]
    [InlineData("   ")]
    public void CreatePayment_MethodNotAllowed_ReturnsBadRequest(string method)
    {
        IActionResult result = CreateController().CreatePayment(Request(method));

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("efectivo", badRequest.Value!.ToString());
    }

    [Fact]
    public void CreatePayment_CashWithReference_ReturnsBadRequest()
    {
        IActionResult result = CreateController().CreatePayment(Request("efectivo", "TRX-0001"));

        BadRequestObjectResult badRequest = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("Only a bank transfer", badRequest.Value!.ToString());
    }

    [Fact]
    public void CreatePayment_CashWithBlankReference_IgnoresReference()
    {
        // A blank reference is treated as no reference, so the next rule (the user id) answers
        IActionResult result = CreateController().CreatePayment(Request("efectivo", "   "));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Theory]
    [InlineData("EFECTIVO")]
    [InlineData(" Transferencia ")]
    public void CreatePayment_MethodWithDifferentCaseOrSpaces_IsAccepted(string method)
    {
        // The method passes validation, so the next rule (the user id) answers
        IActionResult result = CreateController().CreatePayment(Request(method));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void CreatePayment_TokenWithoutUserId_ReturnsUnauthorized()
    {
        ClaimsPrincipal user = UserWithClaims(new Claim(ClaimTypes.Email, "admin@cacc.com"));

        IActionResult result = CreateController(user).CreatePayment(Request("efectivo"));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public void CreatePayment_UserIdNotNumeric_ReturnsUnauthorized()
    {
        ClaimsPrincipal user = UserWithClaims(new Claim(ClaimTypes.NameIdentifier, "admin"));

        IActionResult result = CreateController(user).CreatePayment(Request("transferencia", "TRX-0001"));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void GetPaymentsByPlayer_PlayerIdNotPositive_ReturnsBadRequest(long playerId)
    {
        IActionResult result = CreateController().GetPaymentsByPlayer(playerId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void GetLatestPayments_CountOutOfRange_ReturnsBadRequest(int count)
    {
        IActionResult result = CreateController().GetLatestPayments(count);

        Assert.IsType<BadRequestObjectResult>(result);
    }
}
