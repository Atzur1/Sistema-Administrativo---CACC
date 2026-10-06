namespace ApiGestion.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// HU-023: búsqueda de alumnos por DNI o apellido en el panel de Cuotas. El controller
// valida el término y mapea al DTO; el SQL se prueba aparte (PlayerSearchIntegrationTests).
public class PlayersControllerSearchTests
{
    private static (PlayersController controller, FakePlayerDAO players) CreateController()
    {
        FakePlayerDAO players = new FakePlayerDAO();

        PlayersController controller = new PlayersController(
            NullLogger<PlayersController>.Instance,
            new FakeJugadoresDao(),
            new DiscountDao(""),
            players,
            new EnrollmentFeeDAO(""),
            new FakeArancelesDao(),
            new FakeCategoriasDao());

        return (controller, players);
    }

    [Fact]
    public void Search_MapsEveryMatchToTheDto()
    {
        var (controller, players) = CreateController();
        players.Results.Add(new PlayerSearchResult { Id = 10, FirstName = "NICOLÁS", LastName = "BAZÁN", Dni = "47970803", CategoryName = "AFA 20067" });

        IActionResult result = controller.SearchPlayers("bazan");

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        PlayerSearchResultDto dto = Assert.Single(Assert.IsAssignableFrom<IEnumerable<PlayerSearchResultDto>>(ok.Value));
        Assert.Equal(10, dto.Id);
        Assert.Equal("NICOLÁS", dto.FirstName);
        Assert.Equal("BAZÁN", dto.LastName);
        Assert.Equal("47970803", dto.Dni);
        Assert.Equal("AFA 20067", dto.CategoryName);
    }

    [Fact]
    public void Search_WithoutMatches_ReturnsAnEmptyList()
    {
        var (controller, _) = CreateController();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(controller.SearchPlayers("inexistente"));

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<PlayerSearchResultDto>>(ok.Value));
    }

    [Fact]
    public void Search_TrimsTheTermAndCapsTheResults()
    {
        var (controller, players) = CreateController();

        controller.SearchPlayers("  acosta  ");

        Assert.Equal("acosta", players.LastTerm);
        Assert.Equal(PlayersController.MaxSearchResults, players.LastMaxResults);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    public void Search_WithATermTooShort_IsRejectedWithoutQuerying(string? term)
    {
        var (controller, players) = CreateController();

        IActionResult result = controller.SearchPlayers(term);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(players.LastTerm);
    }

    [Fact]
    public void Search_WithATermTooLong_IsRejected()
    {
        var (controller, players) = CreateController();

        IActionResult result = controller.SearchPlayers(new string('a', PlayersController.MaxSearchTermLength + 1));

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(players.LastTerm);
    }

    [Fact]
    public void Search_WhenTheDatabaseFails_Returns500WithASpanishMessage()
    {
        var (controller, players) = CreateController();
        players.Failure = new InvalidOperationException("sin conexión");

        ObjectResult result = Assert.IsType<ObjectResult>(controller.SearchPlayers("acosta"));

        Assert.Equal(500, result.StatusCode);
        Assert.Contains("No se pudo realizar la búsqueda", result.Value!.ToString());
    }

    [Fact]
    public void Search_IsAGetUnderSearch_RestrictedToTheAdministrativeRoles()
    {
        MethodInfo method = typeof(PlayersController).GetMethod(nameof(PlayersController.SearchPlayers))!;

        Assert.Equal("search", Assert.Single(method.GetCustomAttributes<HttpGetAttribute>()).Template);
        Assert.Equal("1,2", Assert.Single(method.GetCustomAttributes<AuthorizeAttribute>()).Roles);
        Assert.Null(method.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    // ---- Armado del término (sin base) ----

    [Theory]
    [InlineData("47970803", "47970803")]
    [InlineData("47.970.803", "47970803")]
    [InlineData(" 47 970 803 ", "47970803")]
    [InlineData("4797", "4797")]
    [InlineData("acosta", null)]
    [InlineData("o'neill", null)]
    [InlineData("47a", null)]
    [InlineData("..", null)]
    public void OnlyDniDigits_TellsADniFromALastName(string term, string? expected)
    {
        Assert.Equal(expected, PlayerDAO.OnlyDniDigits(term));
    }

    [Theory]
    [InlineData("acosta", "acosta")]
    [InlineData("100%", @"100\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData("[a]", @"\[a]")]
    [InlineData(@"a\b", @"a\\b")]
    public void EscapeLike_SearchesWildcardsAsPlainCharacters(string value, string expected)
    {
        Assert.Equal(expected, PlayerDAO.EscapeLike(value));
    }

    private class FakePlayerDAO : PlayerDAO
    {
        public List<PlayerSearchResult> Results { get; } = new();
        public Exception? Failure { get; set; }
        public string? LastTerm { get; private set; }
        public int LastMaxResults { get; private set; }

        public FakePlayerDAO() : base("") { }

        public override IReadOnlyList<PlayerSearchResult> SearchPlayersForFeeManagement(string searchTerm, int maxResults)
        {
            LastTerm = searchTerm;
            LastMaxResults = maxResults;
            if (Failure != null) throw Failure;
            return Results;
        }
    }

    private class FakeJugadoresDao : IJugadoresDao
    {
        public IReadOnlyList<JugadorResumen> ListarJugadores() => new List<JugadorResumen>();
        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => null;
    }

    private class FakeArancelesDao : IArancelesDao
    {
        public decimal? ObtenerMontoVigente(string genero, DateTime fecha) => null;
        public decimal? ObtenerMontoVigente(string genero, int idCategoria, DateTime fecha) => null;
        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => new List<ArancelHistorialItem>();
        public ArancelResumen ObtenerResumen() => new ArancelResumen();
        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde) { }
        public void ProgramarArancel(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
            => throw new NotSupportedException();
        public Arancel? ObtenerPorId(SqlConnection conexion, SqlTransaction transaccion, int idArancel)
            => throw new NotSupportedException();
        public DateTime? ObtenerVigenteDesdeEnMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int anio, int mes)
            => throw new NotSupportedException();
        public DateTime? ObtenerSiguienteVigenteDesde(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde)
            => throw new NotSupportedException();
        public void EliminarArancel(SqlConnection conexion, SqlTransaction transaccion, int idArancel)
            => throw new NotSupportedException();
    }

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria>();
    }
}

// HU-023: el SQL de la búsqueda sobre una base real. Crea un jugador propio dentro de una
// transacción que se revierte, así no depende de los datos de la base ni deja nada.
public class PlayerSearchIntegrationTests
{
    [IntegracionFact]
    public void Search_FindsByLastNameIgnoringAccentsAndCase_AndByFullOrPartialDni()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            long playerId = InsertPlayer(connection, transaction, "QWZÁRTEZ", "PRUEBA", "99887766");

            Assert.Contains(PlayerDAO.SearchPlayers(connection, transaction, "qwzartez", 50), p => p.Id == playerId);
            Assert.Contains(PlayerDAO.SearchPlayers(connection, transaction, "WZÁR", 50), p => p.Id == playerId);
            Assert.Contains(PlayerDAO.SearchPlayers(connection, transaction, "99.887.766", 50), p => p.Id == playerId);

            PlayerSearchResult byPartialDni = Assert.Single(PlayerDAO.SearchPlayers(connection, transaction, "9988776", 50), p => p.Id == playerId);
            Assert.Equal("QWZÁRTEZ", byPartialDni.LastName);
            Assert.Equal("PRUEBA", byPartialDni.FirstName);
            Assert.False(string.IsNullOrEmpty(byPartialDni.CategoryName));

            Assert.Empty(PlayerDAO.SearchPlayers(connection, transaction, "qwz%rtez", 50));
            Assert.Empty(PlayerDAO.SearchPlayers(connection, transaction, "zzzzzzzzzzqx", 50));
        }
        finally
        {
            transaction.Rollback();
        }
    }

    [IntegracionFact]
    public void Search_NeverReturnsMoreThanTheCap()
    {
        using var connection = new SqlConnection(Environment.GetEnvironmentVariable(IntegracionFactAttribute.Variable));
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            for (int i = 0; i < 3; i++)
            {
                InsertPlayer(connection, transaction, "QWZTOPE", $"PRUEBA{i}", $"9988770{i}");
            }

            Assert.Equal(2, PlayerDAO.SearchPlayers(connection, transaction, "qwztope", 2).Count);
        }
        finally
        {
            transaction.Rollback();
        }
    }

    private static long InsertPlayer(SqlConnection connection, SqlTransaction transaction, string lastName, string firstName, string dni)
    {
        using var category = new SqlCommand("SELECT TOP 1 PK_id_categoria FROM CATEGORIAS ORDER BY PK_id_categoria", connection, transaction);
        var player = new Player
        {
            FirstName = firstName,
            LastName = lastName,
            Dni = dni,
            BirthDate = new DateTime(2010, 1, 1),
            Gender = "Masculino",
            CategoryId = Convert.ToInt64(category.ExecuteScalar()),
            JoinDate = new DateTime(2026, 10, 1)
        };

        int personId = PlayerDAO.InsertPerson(connection, transaction, player);
        return PlayerDAO.InsertPlayer(connection, transaction, personId, player);
    }
}
