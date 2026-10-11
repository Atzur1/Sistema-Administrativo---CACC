namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using EntityLibrary;
using ServiceLibrary;

// A player's enrollment fee and its payments (HU-033). Kept apart from the monthly
// fees: it has its own balance and accepts partial payments the same way. The rules
// live in ServiceLibrary.InscripcionesService.
[ApiController]
[Route("api/players/{playerId}/enrollment")]
[Authorize(Roles = "1,2")]
public class PlayerEnrollmentController : ControllerBase
{
    private readonly ILogger<PlayerEnrollmentController> _logger;
    private readonly IInscripcionesService _inscripcionesService;

    public PlayerEnrollmentController(ILogger<PlayerEnrollmentController> logger, IInscripcionesService inscripcionesService)
    {
        _logger = logger;
        _inscripcionesService = inscripcionesService;
    }

    // Mismo claim que usa AuditActorMiddleware: quien cobra sale del JWT, nunca del body.
    private int? IdUsuarioAutenticado() =>
        int.TryParse(User?.FindFirst("idUsuario")?.Value, out var id) && id > 0 ? id : null;

    [HttpGet]
    public IActionResult GetEnrollment(long playerId)
    {
        Enrollment? enrollment = _inscripcionesService.ObtenerDelJugador(playerId);
        if (enrollment == null)
        {
            return NotFound(new { mensaje = $"El jugador #{playerId} no tiene inscripción." });
        }

        return Ok(MapToDto(enrollment));
    }

    [HttpPost("payments")]
    public IActionResult CreateEnrollmentPayment(long playerId, EnrollmentPaymentRequestDTO request)
    {
        int? idUsuario = IdUsuarioAutenticado();
        if (idUsuario == null)
        {
            return Unauthorized(new { mensaje = "No se pudo identificar al usuario de la sesión. Volvé a iniciar sesión." });
        }

        var resultado = _inscripcionesService.RegistrarPago(
            new SolicitudPagoInscripcion(playerId, request.Amount!.Value, request.PaymentMethod, idUsuario.Value));

        return this.Responder(resultado, payment =>
        {
            _logger.LogInformation("Enrollment payment {PaymentId} of {Amount} registered for player {PlayerId}",
                payment.Id, payment.Amount, playerId);
            return Created($"/api/players/{playerId}/enrollment", MapPaymentToDto(payment));
        });
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
