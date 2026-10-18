namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// HU-033: contract of the enrollment fee endpoints. The SQL that resolves end date
// and status was checked separately against the real database.
public class EnrollmentFeesControllerTests
{
    private static (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) CreateController()
    {
        FakeEnrollmentFeeDAO dao = new FakeEnrollmentFeeDAO();
        return (new EnrollmentFeesController(NullLogger<EnrollmentFeesController>.Instance, dao), dao);
    }

    [Fact]
    public void GetAllEnrollmentFees_MapsEveryFeeWithDatesAsIsoStrings()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        dao.Fees.Add(new EnrollmentFee { Id = 2, Amount = 60000, StartDate = new DateTime(2026, 11, 1), Status = EnrollmentFeeStatus.Scheduled });
        dao.Fees.Add(new EnrollmentFee { Id = 1, Amount = 50000, StartDate = new DateTime(2026, 1, 1), EndDate = new DateTime(2026, 10, 31), Status = EnrollmentFeeStatus.Current });

        List<EnrollmentFeeResponseDTO> result = controller.GetAllEnrollmentFees();

        Assert.Equal(2, result.Count);
        Assert.Equal("2026-11-01", result[0].StartDate);
        Assert.Null(result[0].EndDate);
        Assert.Equal("Scheduled", result[0].Status);
        Assert.Equal("2026-10-31", result[1].EndDate);
        Assert.Equal("Current", result[1].Status);
    }

    [Fact]
    public void GetCurrentEnrollmentFee_WithNoFeeInForce_ReturnsNotFound()
    {
        (EnrollmentFeesController controller, _) = CreateController();

        IActionResult result = controller.GetCurrentEnrollmentFee();

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void GetCurrentEnrollmentFee_ReturnsTheFeeInForce()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        dao.Current = new EnrollmentFee { Id = 1, Amount = 50000, StartDate = new DateTime(2026, 1, 1), Status = EnrollmentFeeStatus.Current };

        IActionResult result = controller.GetCurrentEnrollmentFee();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        EnrollmentFeeResponseDTO fee = Assert.IsType<EnrollmentFeeResponseDTO>(ok.Value);
        Assert.Equal(50000, fee.Amount);
        Assert.Equal("Current", fee.Status);
    }

    [Fact]
    public void CreateEnrollmentFee_WithAFreeStartDate_ReturnsCreatedWithTheStoredFee()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        DateTime futuro = RelojNegocio.Hoy.AddMonths(1);
        EnrollmentFeeRequestDTO request = new EnrollmentFeeRequestDTO { Gender = "Masculino", Amount = 55000, StartDate = futuro.AddHours(15).AddMinutes(30) };

        IActionResult result = controller.CreateEnrollmentFee(request);

        CreatedResult created = Assert.IsType<CreatedResult>(result);
        EnrollmentFeeResponseDTO fee = Assert.IsType<EnrollmentFeeResponseDTO>(created.Value);
        Assert.Equal($"/api/enrollmentfees/{fee.Id}", created.Location);
        Assert.Equal(55000, fee.Amount);
        // The time of day is dropped: a fee is in force from the start of that day
        Assert.Equal(futuro.ToString("yyyy-MM-dd"), fee.StartDate);
        Assert.Single(dao.Fees);
    }

    [Fact]
    public void CreateEnrollmentFee_WithAStartDateAlreadyTaken_ReturnsBadRequestAndCreatesNothing()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        DateTime futuro = RelojNegocio.Hoy.AddMonths(1);
        dao.Fees.Add(new EnrollmentFee { Id = 1, Amount = 50000, StartDate = futuro });

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Gender = "Masculino", Amount = 60000, StartDate = futuro });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(dao.Fees);
    }

    // Regla del club: ningún arancel (tampoco el de inscripción) se programa con fecha anterior a hoy.
    [Fact]
    public void CreateEnrollmentFee_WithAPastStartDate_ReturnsBadRequestAndCreatesNothing()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Gender = "Masculino", Amount = 60000, StartDate = RelojNegocio.Hoy.AddDays(-1) });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(dao.Fees);
    }

    [Fact]
    public void CreateEnrollmentFee_StartingToday_IsAccepted()
    {
        (EnrollmentFeesController controller, _) = CreateController();

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Gender = "Masculino", Amount = 60000, StartDate = RelojNegocio.Hoy });

        Assert.IsType<CreatedResult>(result);
    }

    [Fact]
    public void RequestDTO_WithANegativeAmount_IsInvalid()
    {
        EnrollmentFeeRequestDTO request = new EnrollmentFeeRequestDTO { Gender = "Masculino", Amount = -100, StartDate = new DateTime(2026, 10, 1) };

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(EnrollmentFeeRequestDTO.Amount)));
    }

    // Monto 0 = ese género no paga inscripción desde esa fecha.
    [Fact]
    public void RequestDTO_WithAnAmountOfZero_IsValid()
    {
        Assert.Empty(Validate(new EnrollmentFeeRequestDTO { Gender = "Femenino", Amount = 0, StartDate = new DateTime(2026, 10, 1) }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Otro")]
    [InlineData("masculino ")]
    public void RequestDTO_WithoutAValidGender_IsInvalid(string? gender)
    {
        EnrollmentFeeRequestDTO request = new EnrollmentFeeRequestDTO { Gender = gender, Amount = 50000, StartDate = new DateTime(2026, 10, 1) };

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(EnrollmentFeeRequestDTO.Gender)));
    }

    [Fact]
    public void RequestDTO_WithoutAmountOrStartDate_IsInvalid()
    {
        List<ValidationResult> results = Validate(new EnrollmentFeeRequestDTO());

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EnrollmentFeeRequestDTO.Amount)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(EnrollmentFeeRequestDTO.StartDate)));
    }

    [Fact]
    public void RequestDTO_WithAPositiveAmountAndAStartDate_IsValid()
    {
        Assert.Empty(Validate(new EnrollmentFeeRequestDTO { Gender = "Masculino", Amount = 50000, StartDate = new DateTime(2026, 10, 1) }));
    }

    // Cada género tiene su propia historia de aranceles: el mismo día puede empezar uno masculino y uno femenino.
    [Fact]
    public void CreateEnrollmentFee_OnADayTakenByTheOtherGender_IsAccepted()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        DateTime futuro = RelojNegocio.Hoy.AddMonths(1);
        dao.Fees.Add(new EnrollmentFee { Id = 1, Gender = "Masculino", Amount = 50000, StartDate = futuro });

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Gender = "Femenino", Amount = 30000, StartDate = futuro });

        EnrollmentFeeResponseDTO fee = Assert.IsType<EnrollmentFeeResponseDTO>(Assert.IsType<CreatedResult>(result).Value);
        Assert.Equal("Femenino", fee.Gender);
        Assert.Equal(2, dao.Fees.Count);
    }

    [Fact]
    public void CreateEnrollmentFee_WithADayTakenByTheSameGender_ReturnsBadRequest()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        DateTime futuro = RelojNegocio.Hoy.AddMonths(1);
        dao.Fees.Add(new EnrollmentFee { Id = 1, Gender = "Femenino", Amount = 30000, StartDate = futuro });

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Gender = "Femenino", Amount = 40000, StartDate = futuro });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(dao.Fees);
    }

    [Fact]
    public void CreateEnrollmentFee_WithAZeroAmount_IsStored_SoTheGenderStopsPaying()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Gender = "Femenino", Amount = 0, StartDate = RelojNegocio.Hoy.AddDays(1) });

        Assert.IsType<CreatedResult>(result);
        Assert.Equal(0, dao.Fees.Single().Amount);
    }

    [Fact]
    public void GetCurrentEnrollmentFee_AsksForTheGenderRequested()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        dao.Current = new EnrollmentFee { Id = 1, Gender = "Masculino", Amount = 50000, StartDate = new DateTime(2026, 1, 1), Status = EnrollmentFeeStatus.Current };
        dao.Current = new EnrollmentFee { Id = 2, Gender = "Femenino", Amount = 30000, StartDate = new DateTime(2026, 1, 1), Status = EnrollmentFeeStatus.Current };

        EnrollmentFeeResponseDTO femenino = Assert.IsType<EnrollmentFeeResponseDTO>(Assert.IsType<OkObjectResult>(controller.GetCurrentEnrollmentFee("Femenino")).Value);
        EnrollmentFeeResponseDTO porDefecto = Assert.IsType<EnrollmentFeeResponseDTO>(Assert.IsType<OkObjectResult>(controller.GetCurrentEnrollmentFee()).Value);

        Assert.Equal(30000, femenino.Amount);
        Assert.Equal("Masculino", porDefecto.Gender);
        Assert.IsType<BadRequestObjectResult>(controller.GetCurrentEnrollmentFee("Otro"));
    }

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(EnrollmentFeesController).GetCustomAttribute<AuthorizeAttribute>());
    }

    private static List<ValidationResult> Validate(object model)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    // ---- Fakes ----

    private class FakeEnrollmentFeeDAO : EnrollmentFeeDAO
    {
        public List<EnrollmentFee> Fees { get; set; } = new();
        public Dictionary<string, EnrollmentFee> PorGenero { get; } = new();
        // El arancel masculino: lo que estas pruebas usaban antes de que la inscripción dependiera del género.
        public EnrollmentFee? Current
        {
            get => PorGenero.GetValueOrDefault(Generos.Masculino);
            set { if (value != null) PorGenero[value.Gender] = value; }
        }

        public FakeEnrollmentFeeDAO() : base("")
        {
        }

        public override List<EnrollmentFee> GetAllEnrollmentFees()
        {
            return Fees;
        }

        public override EnrollmentFee? GetCurrentEnrollmentFee(string gender)
        {
            return PorGenero.GetValueOrDefault(gender);
        }

        public override EnrollmentFee? GetEnrollmentFeeByStartDate(string gender, DateTime startDate)
        {
            return Fees.FirstOrDefault(f => f.Gender == gender && f.StartDate.Date == startDate.Date);
        }

        public override EnrollmentFee CreateEnrollmentFee(EnrollmentFee fee)
        {
            fee.Id = Fees.Count + 1;
            Fees.Add(fee);
            return fee;
        }
    }
}
