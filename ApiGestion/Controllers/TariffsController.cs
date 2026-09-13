namespace ApiGestion.Controllers;

using Microsoft.AspNetCore.Mvc;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

[ApiController]
[Route("api/[controller]")]
public class TariffsController : ControllerBase
{
    private static readonly string[] Branches = { "M", "F" };

    private readonly ILogger<TariffsController> _logger;
    private readonly ITariffDao _tariffDao;

    public TariffsController(ILogger<TariffsController> logger, ITariffDao tariffDao)
    {
        _logger = logger;
        _tariffDao = tariffDao;
    }

    // Feeds the "Historial y aranceles programados" table: every tariff ever
    // scheduled for either branch.
    [HttpGet]
    public IActionResult GetTariffHistory()
    {
        List<Tariff> tariffs = _tariffDao.GetTariffHistory();
        _logger.LogInformation("Tariff history returned: {Count}", tariffs.Count);

        return Ok(tariffs.Select(MapToDto).ToList());
    }

    // Feeds the header metrics: the tariff in force today for each branch.
    [HttpGet("current")]
    public IActionResult GetCurrentTariffs()
    {
        DateTime today = DateTime.Today;

        List<TariffResponseDTO> current = Branches
            .Select(branch => _tariffDao.GetCurrentTariffByBranch(branch, today))
            .Where(tariff => tariff != null)
            .Select(tariff => MapToDto(tariff!))
            .ToList();

        return Ok(current);
    }

    // Schedules a new tariff for one branch. It never rewrites a fee already
    // issued: it only opens a validity period starting on ValidFrom, closing
    // the branch's previous one the day before.
    [HttpPost]
    public IActionResult ScheduleTariff([FromBody] ScheduleTariffRequestDTO request)
    {
        if (request.Branch != "M" && request.Branch != "F")
        {
            return BadRequest("Branch must be either 'M' or 'F'.");
        }

        if (request.Amount <= 0)
        {
            return BadRequest("Amount must be greater than zero.");
        }

        if (!DateTime.TryParse(request.ValidFrom, out DateTime validFrom))
        {
            return BadRequest("ValidFrom must be a valid date.");
        }

        try
        {
            Tariff scheduled = _tariffDao.ScheduleTariff(new Tariff
            {
                Branch = request.Branch,
                Amount = request.Amount,
                ValidFrom = validFrom
            });

            _logger.LogInformation("Tariff scheduled for branch {Branch} starting {ValidFrom}",
                scheduled.Branch, scheduled.ValidFrom);

            return Ok(MapToDto(scheduled));
        }
        catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
        {
            return BadRequest(ex.Message);
        }
    }

    private TariffResponseDTO MapToDto(Tariff tariff)
    {
        return new TariffResponseDTO
        {
            Id = tariff.Id,
            Branch = tariff.Branch,
            Amount = tariff.Amount,
            ValidFrom = tariff.ValidFrom.ToString("yyyy-MM-dd"),
            ValidTo = tariff.ValidTo?.ToString("yyyy-MM-dd"),
            IsActive = tariff.IsActive
        };
    }
}
