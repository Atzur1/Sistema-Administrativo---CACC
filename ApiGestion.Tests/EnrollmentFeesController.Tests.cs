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
        EnrollmentFeeRequestDTO request = new EnrollmentFeeRequestDTO { Amount = 55000, StartDate = new DateTime(2026, 10, 1, 15, 30, 0) };

        IActionResult result = controller.CreateEnrollmentFee(request);

        CreatedResult created = Assert.IsType<CreatedResult>(result);
        EnrollmentFeeResponseDTO fee = Assert.IsType<EnrollmentFeeResponseDTO>(created.Value);
        Assert.Equal($"/api/enrollmentfees/{fee.Id}", created.Location);
        Assert.Equal(55000, fee.Amount);
        // The time of day is dropped: a fee is in force from the start of that day
        Assert.Equal("2026-10-01", fee.StartDate);
        Assert.Single(dao.Fees);
    }

    [Fact]
    public void CreateEnrollmentFee_WithAStartDateAlreadyTaken_ReturnsBadRequestAndCreatesNothing()
    {
        (EnrollmentFeesController controller, FakeEnrollmentFeeDAO dao) = CreateController();
        dao.Fees.Add(new EnrollmentFee { Id = 1, Amount = 50000, StartDate = new DateTime(2026, 10, 1) });

        IActionResult result = controller.CreateEnrollmentFee(new EnrollmentFeeRequestDTO { Amount = 60000, StartDate = new DateTime(2026, 10, 1) });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(dao.Fees);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void RequestDTO_WithAnAmountNotGreaterThanZero_IsInvalid(int amount)
    {
        EnrollmentFeeRequestDTO request = new EnrollmentFeeRequestDTO { Amount = amount, StartDate = new DateTime(2026, 10, 1) };

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(EnrollmentFeeRequestDTO.Amount)));
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
        Assert.Empty(Validate(new EnrollmentFeeRequestDTO { Amount = 50000, StartDate = new DateTime(2026, 10, 1) }));
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
        public EnrollmentFee? Current { get; set; }

        public FakeEnrollmentFeeDAO() : base("")
        {
        }

        public override List<EnrollmentFee> GetAllEnrollmentFees()
        {
            return Fees;
        }

        public override EnrollmentFee? GetCurrentEnrollmentFee()
        {
            return Current;
        }

        public override EnrollmentFee? GetEnrollmentFeeByStartDate(DateTime startDate)
        {
            return Fees.FirstOrDefault(f => f.StartDate.Date == startDate.Date);
        }

        public override EnrollmentFee CreateEnrollmentFee(EnrollmentFee fee)
        {
            fee.Id = Fees.Count + 1;
            Fees.Add(fee);
            return fee;
        }
    }
}
