namespace ApiGestion.Tests;

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// HU-033: registration of a new player and the charges it generates. The SQL of
// PlayerDAO (single transaction, rollback, frozen amounts) was checked separately
// against the real database.
public class PlayersControllerCreatePlayerTests
{
    private const decimal MonthlyFee = 92000;
    private const decimal EnrollmentFeeAmount = 50000;

    private static (PlayersController controller, FakePlayerDAO players, FakeEnrollmentFeeDAO enrollment, FakeArancelesDao aranceles) CreateController()
    {
        FakePlayerDAO players = new FakePlayerDAO();
        FakeEnrollmentFeeDAO enrollment = new FakeEnrollmentFeeDAO();
        FakeArancelesDao aranceles = new FakeArancelesDao();
        FakeCategoriasDao categorias = new FakeCategoriasDao();

        PlayersController controller = new PlayersController(
            NullLogger<PlayersController>.Instance,
            new FakeJugadoresDao(),
            new DiscountDao(""),
            players,
            enrollment,
            aranceles,
            categorias);

        return (controller, players, enrollment, aranceles);
    }

    private static PlayerRequestDTO ValidRequest(string gender)
    {
        return new PlayerRequestDTO
        {
            FirstName = " Juan ",
            LastName = "Pérez",
            Dni = "40123456",
            BirthDate = new DateTime(2010, 5, 20),
            Gender = gender,
            CategoryId = 1
        };
    }

    [Fact]
    public void CreatePlayer_MaleWithBothFeesInForce_ChargesMonthlyAndEnrollmentFee()
    {
        var (controller, players, enrollment, aranceles) = CreateController();
        enrollment.Current = new EnrollmentFee { Id = 1, Amount = EnrollmentFeeAmount, Status = EnrollmentFeeStatus.Current };
        aranceles.Amounts["Masculino"] = MonthlyFee;

        IActionResult result = controller.CreatePlayer(ValidRequest("Masculino"));

        CreatedResult created = Assert.IsType<CreatedResult>(result);
        PlayerResponseDTO player = Assert.IsType<PlayerResponseDTO>(created.Value);
        Assert.Equal($"/api/players/{player.Id}", created.Location);
        Assert.Equal(MonthlyFee, player.MonthlyFeeAmount);
        Assert.Equal(EnrollmentFeeAmount, player.EnrollmentFeeAmount);
        Assert.Equal(MonthlyFee, players.LastMonthlyFee);
        Assert.Equal(EnrollmentFeeAmount, players.LastEnrollmentFee);
        Assert.Equal("Juan", players.Created!.FirstName);
        Assert.Equal(DateTime.Now.Date, players.Created.JoinDate);
    }

    [Fact]
    public void CreatePlayer_Female_NeverChargesAnEnrollmentFee()
    {
        var (controller, players, enrollment, aranceles) = CreateController();
        enrollment.Current = new EnrollmentFee { Id = 1, Amount = EnrollmentFeeAmount, Status = EnrollmentFeeStatus.Current };
        aranceles.Amounts["Femenino"] = 80000;

        IActionResult result = controller.CreatePlayer(ValidRequest("Femenino"));

        PlayerResponseDTO player = Assert.IsType<PlayerResponseDTO>(Assert.IsType<CreatedResult>(result).Value);
        Assert.Null(player.EnrollmentFeeAmount);
        Assert.Null(players.LastEnrollmentFee);
        Assert.Equal(80000, players.LastMonthlyFee);
        Assert.False(enrollment.WasAsked);
    }

    [Fact]
    public void CreatePlayer_MaleWithoutEnrollmentFeeInForce_IsRefusedAndCreatesNothing()
    {
        var (controller, players, _, aranceles) = CreateController();
        aranceles.Amounts["Masculino"] = MonthlyFee;

        IActionResult result = controller.CreatePlayer(ValidRequest("Masculino"));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(players.Created);
    }

    [Fact]
    public void CreatePlayer_WithoutMonthlyFeeInForce_RegistersWithoutChargingIt()
    {
        var (controller, players, _, _) = CreateController();

        IActionResult result = controller.CreatePlayer(ValidRequest("Femenino"));

        PlayerResponseDTO player = Assert.IsType<PlayerResponseDTO>(Assert.IsType<CreatedResult>(result).Value);
        Assert.Null(player.MonthlyFeeAmount);
        Assert.Null(players.LastMonthlyFee);
        Assert.NotNull(players.Created);
    }

    [Fact]
    public void CreatePlayer_LooksUpTheMonthlyFeeAgainstTheLastDayOfTheMonth()
    {
        var (controller, _, _, aranceles) = CreateController();

        controller.CreatePlayer(ValidRequest("Femenino"));

        DateTime today = DateTime.Now.Date;
        Assert.Equal(new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month)), aranceles.LastDate);
    }

    [Fact]
    public void CreatePlayer_WithADniAlreadyRegistered_IsRefusedAndCreatesNothing()
    {
        var (controller, players, enrollment, _) = CreateController();
        enrollment.Current = new EnrollmentFee { Id = 1, Amount = EnrollmentFeeAmount, Status = EnrollmentFeeStatus.Current };
        players.ExistingDnis.Add("40123456");

        IActionResult result = controller.CreatePlayer(ValidRequest("Masculino"));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(players.Created);
    }

    [Fact]
    public void CreatePlayer_WithAnUnknownCategory_IsRefusedAndCreatesNothing()
    {
        var (controller, players, _, _) = CreateController();
        PlayerRequestDTO request = ValidRequest("Femenino");
        request.CategoryId = 999;

        IActionResult result = controller.CreatePlayer(request);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(players.Created);
    }

    [Theory]
    [InlineData("Otro")]
    [InlineData("masculino")]
    public void RequestDTO_WithAGenderOutsideTheTwoSquads_IsInvalid(string gender)
    {
        PlayerRequestDTO request = ValidRequest(gender);

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(PlayerRequestDTO.Gender)));
    }

    [Fact]
    public void RequestDTO_WithABirthDateTodayOrLater_IsInvalid()
    {
        PlayerRequestDTO request = ValidRequest("Masculino");
        request.BirthDate = DateTime.Now.Date;

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(PlayerRequestDTO.BirthDate)));
    }

    [Theory]
    [InlineData("40.123.456")]
    [InlineData("123456")]
    [InlineData("ABC12345")]
    public void RequestDTO_WithAMalformedDni_IsInvalid(string dni)
    {
        PlayerRequestDTO request = ValidRequest("Masculino");
        request.Dni = dni;

        Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(PlayerRequestDTO.Dni)));
    }

    [Fact]
    public void RequestDTO_WithBlankNamesAndNoCategory_IsInvalid()
    {
        PlayerRequestDTO request = ValidRequest("Masculino");
        request.FirstName = "   ";
        request.CategoryId = null;

        List<ValidationResult> results = Validate(request);

        Assert.Contains(results, r => r.MemberNames.Contains(nameof(PlayerRequestDTO.FirstName)));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(PlayerRequestDTO.CategoryId)));
    }

    [Fact]
    public void RequestDTO_Complete_IsValid()
    {
        Assert.Empty(Validate(ValidRequest("Masculino")));
    }

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(PlayersController).GetCustomAttribute<AuthorizeAttribute>());
    }

    private static List<ValidationResult> Validate(object model)
    {
        List<ValidationResult> results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        return results;
    }

    // ---- Fakes ----

    private class FakePlayerDAO : PlayerDAO
    {
        public List<string> ExistingDnis { get; } = new();
        public Player? Created { get; private set; }
        public decimal? LastMonthlyFee { get; private set; }
        public decimal? LastEnrollmentFee { get; private set; }

        public FakePlayerDAO() : base("")
        {
        }

        public override bool ExistsPersonByDni(string dni)
        {
            return ExistingDnis.Contains(dni);
        }

        public override Player CreatePlayer(Player player, decimal? monthlyFeeAmount, decimal? enrollmentFeeAmount)
        {
            player.Id = 42;
            Created = player;
            LastMonthlyFee = monthlyFeeAmount;
            LastEnrollmentFee = enrollmentFeeAmount;
            return player;
        }
    }

    private class FakeEnrollmentFeeDAO : EnrollmentFeeDAO
    {
        public EnrollmentFee? Current { get; set; }
        public bool WasAsked { get; private set; }

        public FakeEnrollmentFeeDAO() : base("")
        {
        }

        public override EnrollmentFee? GetCurrentEnrollmentFee()
        {
            WasAsked = true;
            return Current;
        }
    }

    private class FakeArancelesDao : IArancelesDao
    {
        public Dictionary<string, decimal> Amounts { get; } = new();
        public DateTime? LastDate { get; private set; }

        public decimal? ObtenerMontoVigente(string genero, DateTime fecha)
        {
            LastDate = fecha;
            return Amounts.TryGetValue(genero, out decimal amount) ? amount : null;
        }

        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => new List<ArancelHistorialItem>();

        public ArancelResumen ObtenerResumen() => new ArancelResumen();

        public void ProgramarArancel(string genero, decimal monto, DateTime vigenteDesde)
        {
        }

        public void ProgramarArancel(SqlConnection conexion, SqlTransaction transaccion, string genero, decimal monto, DateTime vigenteDesde)
            => throw new NotSupportedException();
    }

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria>
        {
            new() { IdCategoria = 1, Nombre = "AFA 20067" },
            new() { IdCategoria = 13, Nombre = "Femenino" }
        };
    }

    private class FakeJugadoresDao : IJugadoresDao
    {
        public IReadOnlyList<JugadorResumen> ListarJugadores() => new List<JugadorResumen>();

        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => null;
    }
}
