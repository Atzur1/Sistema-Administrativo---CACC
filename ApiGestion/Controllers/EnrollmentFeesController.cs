namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// Enrollment fee (HU-033), one history per gender. Scheduling a new fee never touches
// fees already charged: each enrollment keeps the amount in force the day the
// player joined. A gender with no fee in force (or with one of 0) is not charged.
// Sin [Authorize] a nivel de clase: se combinaría con AND contra el de cada
// acción y dejaría afuera al rol que no está en ambos (ver ReportesController).
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EnrollmentFeesController : ControllerBase
{
    private readonly ILogger<EnrollmentFeesController> _logger;
    private readonly EnrollmentFeeDAO _enrollmentFeeDAO;

    public EnrollmentFeesController(ILogger<EnrollmentFeesController> logger, EnrollmentFeeDAO enrollmentFeeDAO)
    {
        _logger = logger;
        _enrollmentFeeDAO = enrollmentFeeDAO;
    }

    // Feeds the history table: past, current and scheduled fees. También la usa
    // "Cuotas y Pagos" (rol 2) para calcular el monto vigente de matrícula.
    [Authorize(Roles = "1,2")]
    [HttpGet]
    public List<EnrollmentFeeResponseDTO> GetAllEnrollmentFees()
    {
        return _enrollmentFeeDAO.GetAllEnrollmentFees().Select(MapToDto).ToList();
    }

    [Authorize(Roles = "1")]
    [HttpGet("current")]
    public IActionResult GetCurrentEnrollmentFee([FromQuery] string gender = Generos.Masculino)
    {
        if (!Generos.Validos.Contains(gender))
        {
            return BadRequest(new { mensaje = "El género debe ser Masculino o Femenino." });
        }

        EnrollmentFee? fee = _enrollmentFeeDAO.GetCurrentEnrollmentFee(gender);
        if (fee == null)
        {
            return NotFound(new { mensaje = $"Todavía no hay un arancel de inscripción vigente para el género {gender}." });
        }

        return Ok(MapToDto(fee));
    }

    [Authorize(Roles = "1")]
    [HttpPost]
    public IActionResult CreateEnrollmentFee(EnrollmentFeeRequestDTO request)
    {
        string gender = request.Gender!;
        DateTime startDate = request.StartDate!.Value.Date;

        // Misma regla que el arancel mensual: no se programa con fecha pasada.
        if (startDate < RelojNegocio.Hoy)
        {
            return BadRequest(new { mensaje = $"La fecha de vigencia no puede ser anterior a hoy ({RelojNegocio.Hoy:dd/MM/yyyy})." });
        }

        if (_enrollmentFeeDAO.GetEnrollmentFeeByStartDate(gender, startDate) != null)
        {
            return BadRequest(new { mensaje = $"Ya hay un arancel de inscripción {gender.ToLowerInvariant()} que rige desde el {startDate:dd/MM/yyyy}." });
        }

        EnrollmentFee created = _enrollmentFeeDAO.CreateEnrollmentFee(new EnrollmentFee
        {
            Gender = gender,
            Amount = request.Amount!.Value,
            StartDate = startDate
        });

        _logger.LogInformation("Enrollment fee {FeeId} ({Gender}) scheduled from {StartDate}", created.Id, gender, startDate);

        return Created($"/api/enrollmentfees/{created.Id}", MapToDto(created));
    }

    private static EnrollmentFeeResponseDTO MapToDto(EnrollmentFee fee)
    {
        return new EnrollmentFeeResponseDTO
        {
            Id = fee.Id,
            Gender = fee.Gender,
            Amount = fee.Amount,
            StartDate = fee.StartDate.ToString("yyyy-MM-dd"),
            EndDate = fee.EndDate?.ToString("yyyy-MM-dd"),
            Status = fee.Status.ToString(),
            ResponsibleName = fee.ResponsibleName,
            ResponsibleSurname = fee.ResponsibleSurname,
            RegisteredAtUtc = fee.RegisteredAtUtc
        };
    }
}
