namespace ApiGestion.Controllers;

using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

[ApiController]
[Route("api/[controller]")]
public class PaymentsController : ControllerBase
{
    private const int DefaultLatestCount = 10;
    private const int MaxLatestCount = 100;

    // Stored lowercase, matching the existing rows and the CK_PAGOS_metodo_pago constraint
    private const string CashMethod = "efectivo";
    private const string TransferMethod = "transferencia";

    private readonly ILogger<PaymentsController> _logger;
    private readonly PaymentDao _paymentDao;
    private readonly PlayerDao _playerDao;

    public PaymentsController(ILogger<PaymentsController> logger, PaymentDao paymentDao, PlayerDao playerDao)
    {
        _logger = logger;
        _paymentDao = paymentDao;
        _playerDao = playerDao;
    }

    // Feeds the "Últimos pagos" panel
    [HttpGet("latest")]
    public IActionResult GetLatestPayments(int count = DefaultLatestCount)
    {
        if (count <= 0 || count > MaxLatestCount)
        {
            return BadRequest($"The count must be between 1 and {MaxLatestCount}.");
        }

        List<Payment> payments = _paymentDao.GetLatestPayments(count);
        _logger.LogInformation("Latest payments returned: {Count}", payments.Count);

        return Ok(payments.Select(MapToDto).ToList());
    }

    // Feeds the "Pendientes de cobro" panel. Returns an empty list while monthly
    // fee generation is not implemented: no row in PAGOS is in an unpaid state.
    [HttpGet("pending")]
    public IActionResult GetPendingFees()
    {
        List<Payment> pendingFees = _paymentDao.GetPendingFees();
        _logger.LogInformation("Pending fees returned: {Count}", pendingFees.Count);

        return Ok(pendingFees.Select(MapToDto).ToList());
    }

    // Feeds the payment form: the periods a player already paid cannot be collected again
    [HttpGet("player/{playerId}")]
    public IActionResult GetPaymentsByPlayer(long playerId)
    {
        if (playerId <= 0)
        {
            return BadRequest("The player id must be greater than zero.");
        }

        if (_playerDao.GetPlayerById(playerId) == null)
        {
            return NotFound();
        }

        List<Payment> payments = _paymentDao.GetPaymentsByPlayer(playerId);
        _logger.LogInformation("Payments returned for player {PlayerId}: {Count}", playerId, payments.Count);

        return Ok(payments.Select(MapToDto).ToList());
    }

    // Feeds the three counters on the page header
    [HttpGet("metrics")]
    public IActionResult GetTreasuryMetrics()
    {
        TreasuryMetrics metrics = _paymentDao.GetTreasuryMetrics();

        return Ok(new TreasuryMetricsResponseDTO
        {
            CollectedThisYear = metrics.CollectedThisYear,
            PaymentsThisMonth = metrics.PaymentsThisMonth,
            PendingCount = metrics.PendingCount
        });
    }

    // Registers a payment (HU-015 / HU-016). The user id in the token is stored as
    // the person who registered it.
    [Authorize]
    [HttpPost]
    public IActionResult CreatePayment(PaymentRequestDTO request)
    {
        string method = request.Method.Trim().ToLowerInvariant();
        if (method != CashMethod && method != TransferMethod)
        {
            return BadRequest($"The payment method must be '{CashMethod}' or '{TransferMethod}'.");
        }

        string? reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        if (reference != null && method != TransferMethod)
        {
            return BadRequest("Only a bank transfer can carry a reference.");
        }

        // Tokens issued before the user id claim existed do not identify who registers the payment
        if (!long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out long userId))
        {
            return Unauthorized("The token does not identify the user. Please log in again.");
        }

        if (_playerDao.GetPlayerById(request.PlayerId) == null)
        {
            return BadRequest($"No player exists with id {request.PlayerId}.");
        }

        DateTime period = new DateTime(request.PeriodYear, request.PeriodMonth, 1);
        if (_paymentDao.GetPaymentByPlayerAndPeriod(request.PlayerId, period) != null)
        {
            return BadRequest($"The fee for {period:yyyy-MM} is already paid for player {request.PlayerId}.");
        }

        Payment payment = new Payment
        {
            PlayerId = request.PlayerId,
            Period = period,
            FinalAmount = request.Amount,
            Method = method,
            Reference = reference,
            RegisteredByUserId = userId
        };

        Payment createdPayment = _paymentDao.CreatePayment(payment);
        _logger.LogInformation("Payment {PaymentId} registered for player {PlayerId} by user {UserId}",
            createdPayment.Id, createdPayment.PlayerId, userId);

        return Created($"/api/payments/{createdPayment.Id}", MapToDto(createdPayment));
    }

    private PaymentResponseDTO MapToDto(Payment payment)
    {
        return new PaymentResponseDTO
        {
            Id = payment.Id,
            PlayerId = payment.PlayerId,
            PlayerName = payment.PlayerName,
            Category = payment.Category,
            Amount = payment.FinalAmount,
            PaymentDate = payment.PaymentDate?.ToString("yyyy-MM-dd"),
            DueDate = payment.DueDate?.ToString("yyyy-MM-dd"),
            Period = payment.Period?.ToString("yyyy-MM"),
            Method = payment.Method,
            IsPaid = payment.IsPaid,
            Reference = payment.Reference,
            RegisteredAt = payment.RegisteredAt?.ToString("yyyy-MM-ddTHH:mm:ss")
        };
    }
}
