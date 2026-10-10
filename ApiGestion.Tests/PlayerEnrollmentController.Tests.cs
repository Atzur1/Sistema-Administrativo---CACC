namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// HU-033: paying a player's enrollment fee, fully or in parts. The SQL of
// EnrollmentDAO (lock on the pending row, delete when paid off) was checked
// separately against the real database.
public class PlayerEnrollmentControllerTests
{
    private const long PlayerId = 6;

    private const int OperatorId = 7;

    private static (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) CreateController(EnrollmentFee? currentFee = null, string gender = "Masculino", bool playerExists = true, bool authenticated = true)
    {
        FakeEnrollmentDAO dao = new FakeEnrollmentDAO();
        FakeEnrollmentFeeDAO feeDao = new FakeEnrollmentFeeDAO { Current = currentFee };
        FakeJugadoresDao jugadores = new FakeJugadoresDao { Player = playerExists ? new JugadorResumen { IdJugador = (int)PlayerId, Genero = gender } : null };
        PlayerEnrollmentController controller = new PlayerEnrollmentController(NullLogger<PlayerEnrollmentController>.Instance, dao, feeDao, jugadores);
        ClaimsIdentity identity = authenticated ? new ClaimsIdentity(new[] { new Claim("idUsuario", OperatorId.ToString()) }, "test") : new ClaimsIdentity();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return (controller, dao);
    }

    private static Enrollment PendingEnrollment(decimal balance)
    {
        Enrollment enrollment = new Enrollment { PlayerId = PlayerId, Amount = 50000, PendingBalance = balance, DueDate = new DateTime(2026, 9, 27) };
        if (balance < 50000)
        {
            enrollment.Payments.Add(new EnrollmentPayment { Id = 1, Amount = 50000 - balance, PaymentMethod = "Efectivo", PaymentDate = new DateTime(2026, 9, 27) });
        }
        return enrollment;
    }

    [Fact]
    public void GetEnrollment_WithoutEnrollmentFee_ReturnsNotFound()
    {
        (PlayerEnrollmentController controller, _) = CreateController();

        Assert.IsType<NotFoundObjectResult>(controller.GetEnrollment(PlayerId));
    }

    [Fact]
    public void GetEnrollment_ReturnsAmountPaidAndBalance()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();
        dao.Enrollment = PendingEnrollment(30000);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(controller.GetEnrollment(PlayerId));
        EnrollmentResponseDTO enrollment = Assert.IsType<EnrollmentResponseDTO>(ok.Value);

        Assert.Equal(50000, enrollment.Amount);
        Assert.Equal(20000, enrollment.PaidAmount);
        Assert.Equal(30000, enrollment.PendingBalance);
        Assert.Equal("2026-09-27", enrollment.DueDate);
        Assert.Single(enrollment.Payments);
    }

    [Fact]
    public void CreateEnrollmentPayment_Partial_ReturnsCreatedAndPaysThatAmount()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();
        dao.Enrollment = PendingEnrollment(50000);

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 20000, PaymentMethod = " Efectivo " });

        CreatedResult created = Assert.IsType<CreatedResult>(result);
        EnrollmentPaymentResponseDTO payment = Assert.IsType<EnrollmentPaymentResponseDTO>(created.Value);
        Assert.Equal($"/api/players/{PlayerId}/enrollment", created.Location);
        Assert.Equal(20000, payment.Amount);
        Assert.Equal(20000, dao.LastAmount);
        Assert.Equal("Efectivo", dao.LastPaymentMethod);
        Assert.Equal(RelojNegocio.Hoy, dao.LastPaymentDate);
        Assert.Equal(OperatorId, dao.LastUserId);
    }

    [Fact]
    public void CreateEnrollmentPayment_OfTheWholeBalance_IsAccepted()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();
        dao.Enrollment = PendingEnrollment(30000);

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 30000, PaymentMethod = "Transferencia" });

        Assert.IsType<CreatedResult>(result);
        Assert.Equal(30000, dao.LastAmount);
    }

    [Fact]
    public void CreateEnrollmentPayment_AboveTheBalance_IsRefusedAndPaysNothing()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();
        dao.Enrollment = PendingEnrollment(30000);

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 30000.01m, PaymentMethod = "Efectivo" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(dao.LastAmount);
    }

    [Fact]
    public void CreateEnrollmentPayment_WhenAlreadyFullyPaid_IsRefused()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();
        dao.Enrollment = PendingEnrollment(0);

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "Efectivo" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(dao.LastAmount);
    }

    [Fact]
    public void CreateEnrollmentPayment_WithoutEnrollmentFeeAndNoExistingEnrollment_ReturnsConflict()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "Efectivo" });

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Null(dao.LastAmount);
    }

    [Fact]
    public void CreateEnrollmentPayment_ForAFemalePlayerWithoutEnrollment_IsRefusedAndCreatesNothing()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController(
            new EnrollmentFee { Amount = 50000, StartDate = new DateTime(2026, 1, 1) }, gender: "Femenino");

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "Efectivo" });

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(dao.FirstPaymentCreated);
    }

    [Fact]
    public void CreateEnrollmentPayment_ForAMalePlayerWithoutEnrollment_CreatesItWithTheOperator()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController(
            new EnrollmentFee { Amount = 50000, StartDate = new DateTime(2026, 1, 1) });

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "Efectivo" });

        Assert.IsType<CreatedResult>(result);
        Assert.True(dao.FirstPaymentCreated);
        Assert.Equal(OperatorId, dao.LastUserId);
    }

    [Fact]
    public void CreateEnrollmentPayment_ForAPlayerThatDoesNotExist_ReturnsNotFound()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController(playerExists: false);
        dao.Enrollment = PendingEnrollment(30000);

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "Efectivo" });

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Null(dao.LastAmount);
    }

    [Fact]
    public void CreateEnrollmentPayment_WithoutAnIdentifiedOperator_IsUnauthorized()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController(authenticated: false);
        dao.Enrollment = PendingEnrollment(30000);

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "Efectivo" });

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Null(dao.LastAmount);
    }

    [Fact]
    public void CreateEnrollmentPayment_WhenAnotherPaymentGotInFirst_ReturnsConflict()
    {
        (PlayerEnrollmentController controller, FakeEnrollmentDAO dao) = CreateController();
        dao.Enrollment = PendingEnrollment(30000);
        dao.RefusePayment = true;

        IActionResult result = controller.CreateEnrollmentPayment(PlayerId, new EnrollmentPaymentRequestDTO { Amount = 30000, PaymentMethod = "Efectivo" });

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Theory]
    [InlineData(0, "Efectivo", nameof(EnrollmentPaymentRequestDTO.Amount))]
    [InlineData(-5, "Efectivo", nameof(EnrollmentPaymentRequestDTO.Amount))]
    [InlineData(1000, "Cheque", nameof(EnrollmentPaymentRequestDTO.PaymentMethod))]
    [InlineData(1000, "", nameof(EnrollmentPaymentRequestDTO.PaymentMethod))]
    public void RequestDTO_WithInvalidData_IsInvalid(int amount, string paymentMethod, string invalidMember)
    {
        EnrollmentPaymentRequestDTO request = new EnrollmentPaymentRequestDTO { Amount = amount, PaymentMethod = paymentMethod };

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(invalidMember));
    }

    [Fact]
    public void RequestDTO_WithAPositiveAmountAndAKnownMethod_IsValid()
    {
        Assert.Empty(Validate(new EnrollmentPaymentRequestDTO { Amount = 1000, PaymentMethod = "transferencia" }));
    }

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(PlayerEnrollmentController).GetCustomAttribute<AuthorizeAttribute>());
    }

    private static List<ValidationResult> Validate(object model)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    // ---- Fakes ----

    private class FakeEnrollmentDAO : EnrollmentDAO
    {
        public Enrollment? Enrollment { get; set; }
        public bool RefusePayment { get; set; }
        public decimal? LastAmount { get; private set; }
        public string? LastPaymentMethod { get; private set; }
        public DateTime? LastPaymentDate { get; private set; }
        public int? LastUserId { get; private set; }
        public bool FirstPaymentCreated { get; private set; }

        public FakeEnrollmentDAO() : base("")
        {
        }

        public override Enrollment? GetEnrollmentByPlayerId(long playerId)
        {
            return Enrollment;
        }

        public override EnrollmentPayment? CreateEnrollmentPayment(long playerId, decimal amount, string paymentMethod, DateTime paymentDate, int? idUsuarioRegistro = null)
        {
            if (RefusePayment)
            {
                return null;
            }

            LastAmount = amount;
            LastPaymentMethod = paymentMethod;
            LastPaymentDate = paymentDate;
            LastUserId = idUsuarioRegistro;
            return new EnrollmentPayment { Id = 99, Amount = amount, PaymentMethod = paymentMethod, PaymentDate = paymentDate };
        }

        public override EnrollmentPayment? CreateEnrollmentAndFirstPayment(long playerId, decimal enrollmentAmount, decimal paymentAmount, string paymentMethod, DateTime paymentDate, int? idUsuarioRegistro = null)
        {
            FirstPaymentCreated = true;
            LastAmount = paymentAmount;
            LastUserId = idUsuarioRegistro;
            return new EnrollmentPayment { Id = 100, Amount = paymentAmount, PaymentMethod = paymentMethod, PaymentDate = paymentDate };
        }
    }

    private class FakeJugadoresDao : IJugadoresDao
    {
        public JugadorResumen? Player { get; set; }

        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => Player;
        public IReadOnlyList<JugadorResumen> ListarJugadores() => throw new NotSupportedException();
        public (BajaJugadorResultado Resultado, int CuotasEliminadas) DarDeBaja(int idJugador, DateTime fechaBaja) => throw new NotSupportedException();
        public bool Reactivar(int idJugador) => throw new NotSupportedException();
    }

    private class FakeEnrollmentFeeDAO : EnrollmentFeeDAO
    {
        public EnrollmentFee? Current { get; set; }

        public FakeEnrollmentFeeDAO() : base("") { }

        public override EnrollmentFee? GetCurrentEnrollmentFee() => Current;
    }
}
