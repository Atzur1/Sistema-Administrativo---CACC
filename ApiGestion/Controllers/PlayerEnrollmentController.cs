namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// A player's enrollment fee and its payments (HU-033). Kept apart from the monthly
// fees: it has its own balance and accepts partial payments the same way.
[ApiController]
[Route("api/players/{playerId}/enrollment")]
[Authorize]
public class PlayerEnrollmentController : ControllerBase
{
    private readonly ILogger<PlayerEnrollmentController> _logger;
    private readonly EnrollmentDAO _enrollmentDAO;

    public PlayerEnrollmentController(ILogger<PlayerEnrollmentController> logger, EnrollmentDAO enrollmentDAO)
    {
        _logger = logger;
        _enrollmentDAO = enrollmentDAO;
    }

    [HttpGet]
    public IActionResult GetEnrollment(long playerId)
    {
        Enrollment? enrollment = _enrollmentDAO.GetEnrollmentByPlayerId(playerId);
        if (enrollment == null)
        {
            return NotFound($"Player {playerId} has no enrollment fee.");
        }

        return Ok(MapToDto(enrollment));
    }

    [HttpPost("payments")]
    public IActionResult CreateEnrollmentPayment(long playerId, EnrollmentPaymentRequestDTO request)
    {
        Enrollment? enrollment = _enrollmentDAO.GetEnrollmentByPlayerId(playerId);
        if (enrollment == null)
        {
            return NotFound($"Player {playerId} has no enrollment fee.");
        }

        if (enrollment.PendingBalance == 0)
        {
            return BadRequest($"The enrollment fee of player {playerId} is already fully paid.");
        }

        decimal amount = request.Amount!.Value;
        if (amount > enrollment.PendingBalance)
        {
            return BadRequest($"The amount exceeds the pending balance of the enrollment fee (${enrollment.PendingBalance:N0}).");
        }

        EnrollmentPayment? payment = _enrollmentDAO.CreateEnrollmentPayment(playerId, amount, request.PaymentMethod.Trim(), DateTime.Now.Date);
        if (payment == null)
        {
            _logger.LogWarning("Concurrent enrollment payment rejected for player {PlayerId}", playerId);
            return Conflict("The enrollment fee changed while it was being paid. Reload it and try again.");
        }

        _logger.LogInformation("Enrollment payment {PaymentId} of {Amount} registered for player {PlayerId}", payment.Id, amount, playerId);

        return Created($"/api/players/{playerId}/enrollment", MapPaymentToDto(payment));
    }

    private static EnrollmentResponseDTO MapToDto(Enrollment enrollment)
    {
        return new EnrollmentResponseDTO
        {
            PlayerId = enrollment.PlayerId,
            Amount = enrollment.Amount,
            PaidAmount = enrollment.PaidAmount,
            PendingBalance = enrollment.PendingBalance,
            DueDate = enrollment.DueDate.ToString("yyyy-MM-dd"),
            Payments = enrollment.Payments.Select(MapPaymentToDto).ToList()
        };
    }

    private static EnrollmentPaymentResponseDTO MapPaymentToDto(EnrollmentPayment payment)
    {
        return new EnrollmentPaymentResponseDTO
        {
            Id = payment.Id,
            Amount = payment.Amount,
            PaymentMethod = payment.PaymentMethod,
            PaymentDate = payment.PaymentDate.ToString("yyyy-MM-dd")
        };
    }
}
