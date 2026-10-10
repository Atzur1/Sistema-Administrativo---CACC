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
[Authorize(Roles = "1,2")]
public class PlayerEnrollmentController : ControllerBase
{
    private readonly ILogger<PlayerEnrollmentController> _logger;
    private readonly EnrollmentDAO _enrollmentDAO;
    private readonly EnrollmentFeeDAO _enrollmentFeeDAO;
    private readonly IJugadoresDao _jugadoresDao;

    public PlayerEnrollmentController(ILogger<PlayerEnrollmentController> logger, EnrollmentDAO enrollmentDAO, EnrollmentFeeDAO enrollmentFeeDAO, IJugadoresDao jugadoresDao)
    {
        _logger = logger;
        _enrollmentDAO = enrollmentDAO;
        _enrollmentFeeDAO = enrollmentFeeDAO;
        _jugadoresDao = jugadoresDao;
    }

    // Mismo claim que usa AuditActorMiddleware: quien cobra sale del JWT, nunca del body.
    private int? IdUsuarioAutenticado() =>
        int.TryParse(User?.FindFirst("idUsuario")?.Value, out var id) && id > 0 ? id : null;

    [HttpGet]
    public IActionResult GetEnrollment(long playerId)
    {
        Enrollment? enrollment = _enrollmentDAO.GetEnrollmentByPlayerId(playerId);
        if (enrollment == null)
        {
            return NotFound(new { mensaje = $"El jugador #{playerId} no tiene inscripción." });
        }

        return Ok(MapToDto(enrollment));
    }

    [HttpPost("payments")]
    public IActionResult CreateEnrollmentPayment(long playerId, EnrollmentPaymentRequestDTO request)
    {
        decimal amount = request.Amount!.Value;
        string paymentMethod = request.PaymentMethod.Trim();

        int? idUsuario = IdUsuarioAutenticado();
        if (idUsuario == null)
        {
            return Unauthorized(new { mensaje = "No se pudo identificar al usuario de la sesión. Volvé a iniciar sesión." });
        }

        JugadorResumen? player = playerId is > 0 and <= int.MaxValue ? _jugadoresDao.ObtenerJugadorPorId((int)playerId) : null;
        if (player == null)
        {
            return NotFound(new { mensaje = $"No existe el jugador #{playerId}." });
        }

        Enrollment? enrollment = _enrollmentDAO.GetEnrollmentByPlayerId(playerId);

        // Player has no enrollment row yet: create it on the fly using the current
        // enrollment fee value. This covers players registered before HU-033 and
        // any case where the sign-up flow did not charge the fee.
        if (enrollment == null)
        {
            // La inscripción es solo de la rama masculina (HU-033), la misma regla que el alta del jugador.
            // Una inscripción que ya existe (por ejemplo, cargada antes de esta regla) se sigue pudiendo
            // cobrar más abajo; lo que no se hace es crearle una nueva a quien no le corresponde.
            if (player.Genero != PlayerRequestDTO.MaleGender)
            {
                return BadRequest(new { mensaje = "La inscripción corresponde solo a la rama masculina: este jugador no tiene inscripción para cobrar." });
            }

            EnrollmentFee? currentFee = _enrollmentFeeDAO.GetCurrentEnrollmentFee();
            if (currentFee == null)
            {
                return Conflict(new { mensaje = "No hay un arancel de inscripción vigente. Cargalo en Actualización de aranceles antes de registrar el pago." });
            }

            if (amount > currentFee.Amount)
            {
                return BadRequest(new { mensaje = $"El monto supera el arancel de inscripción (${currentFee.Amount:N0})." });
            }

            EnrollmentPayment? firstPayment = _enrollmentDAO.CreateEnrollmentAndFirstPayment(
                playerId, currentFee.Amount, amount, paymentMethod, RelojNegocio.Hoy, idUsuario);

            if (firstPayment == null)
            {
                _logger.LogWarning("Race condition on first enrollment payment for player {PlayerId}", playerId);
                return Conflict(new { mensaje = "Otra operación creó la inscripción al mismo tiempo. Actualizá la pantalla y volvé a intentar." });
            }

            _logger.LogInformation("Enrollment created and first payment {PaymentId} of {Amount} registered for player {PlayerId}", firstPayment.Id, amount, playerId);

            return Created($"/api/players/{playerId}/enrollment", MapPaymentToDto(firstPayment));
        }

        if (enrollment.PendingBalance == 0)
        {
            return BadRequest(new { mensaje = "La inscripción de este jugador ya está totalmente pagada." });
        }

        if (amount > enrollment.PendingBalance)
        {
            return BadRequest(new { mensaje = $"El monto supera el saldo pendiente de la inscripción (${enrollment.PendingBalance:N0})." });
        }

        EnrollmentPayment? payment = _enrollmentDAO.CreateEnrollmentPayment(playerId, amount, paymentMethod, RelojNegocio.Hoy, idUsuario);
        if (payment == null)
        {
            _logger.LogWarning("Concurrent enrollment payment rejected for player {PlayerId}", playerId);
            return Conflict(new { mensaje = "La inscripción cambió mientras se registraba el pago. Actualizá la pantalla y volvé a intentar." });
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
