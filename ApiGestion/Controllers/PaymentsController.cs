namespace ApiGestion.Controllers;

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

    private readonly ILogger<PaymentsController> _logger;
    private readonly PaymentDao _paymentDao;

    public PaymentsController(ILogger<PaymentsController> logger, PaymentDao paymentDao)
    {
        _logger = logger;
        _paymentDao = paymentDao;
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
            Method = payment.Method,
            IsPaid = payment.IsPaid
        };
    }
}
