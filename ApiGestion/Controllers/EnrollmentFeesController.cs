namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// Enrollment fee of the men's squad (HU-033). Scheduling a new fee never touches
// fees already charged: each enrollment keeps the amount in force the day the
// player joined.
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
    public IActionResult GetCurrentEnrollmentFee()
    {
        EnrollmentFee? fee = _enrollmentFeeDAO.GetCurrentEnrollmentFee();
        if (fee == null)
        {
            return NotFound("No enrollment fee is in force yet.");
        }

        return Ok(MapToDto(fee));
    }

    [Authorize(Roles = "1")]
    [HttpPost]
    public IActionResult CreateEnrollmentFee(EnrollmentFeeRequestDTO request)
    {
        DateTime startDate = request.StartDate!.Value.Date;

        if (_enrollmentFeeDAO.GetEnrollmentFeeByStartDate(startDate) != null)
        {
            return BadRequest($"An enrollment fee already starts on {startDate:yyyy-MM-dd}.");
        }

        EnrollmentFee created = _enrollmentFeeDAO.CreateEnrollmentFee(new EnrollmentFee
        {
            Amount = request.Amount!.Value,
            StartDate = startDate
        });

        _logger.LogInformation("Enrollment fee {FeeId} scheduled from {StartDate}", created.Id, startDate);

        return Created($"/api/enrollmentfees/{created.Id}", MapToDto(created));
    }

    private static EnrollmentFeeResponseDTO MapToDto(EnrollmentFee fee)
    {
        return new EnrollmentFeeResponseDTO
        {
            Id = fee.Id,
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
