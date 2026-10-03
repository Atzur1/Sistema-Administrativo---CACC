namespace ApiGestion.Tests;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// HU-038: VoidDiscount y CancelDiscount — el controller protege correctamente los estados
// y delega en el DAO. La lógica SQL de DiscountDao se verifica por separado con la base real.
public class PlayersControllerVoidDiscountTests
{
    private const long PlayerId = 10;
    private const long DiscountId = 42;

    private static (PlayersController controller, FakeDiscountDao discounts) CreateController(
        Discount? storedDiscount = null)
    {
        FakeDiscountDao discounts = new FakeDiscountDao(storedDiscount);
        PlayersController controller = new PlayersController(
            NullLogger<PlayersController>.Instance,
            new FakeJugadoresDao(),
            discounts,
            new FakePlayerDAO(),
            new FakeEnrollmentFeeDAO(),
            new FakeArancelesDao(),
            new FakeCategoriasDao());

        return (controller, discounts);
    }

    private static Discount CancelledDiscount() => new Discount
    {
        Id = DiscountId,
        PlayerId = PlayerId,
        StartDate = new DateTime(2026, 1, 1),
        EndDate = new DateTime(2026, 12, 31),
        Status = DiscountStatus.Cancelled,
    };

    private static Discount ExpiredDiscount() => new Discount
    {
        Id = DiscountId,
        PlayerId = PlayerId,
        StartDate = new DateTime(2025, 1, 1),
        EndDate = new DateTime(2025, 12, 31),
        Status = DiscountStatus.Expired,
    };

    // ===== VoidDiscount =====

    [Fact]
    public void VoidDiscount_OnACancelledBenefit_ReturnsNoContent()
    {
        var (controller, _) = CreateController(CancelledDiscount());

        IActionResult result = controller.VoidDiscount(PlayerId, DiscountId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public void VoidDiscount_OnACancelledBenefit_DelegatesToDao()
    {
        var (controller, discounts) = CreateController(CancelledDiscount());

        controller.VoidDiscount(PlayerId, DiscountId);

        Assert.True(discounts.VoidCalled);
    }

    [Fact]
    public void VoidDiscount_OnAnExpiredBenefit_ReturnsConflict()
    {
        var (controller, discounts) = CreateController(ExpiredDiscount());

        IActionResult result = controller.VoidDiscount(PlayerId, DiscountId);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.False(discounts.VoidCalled);
    }

    [Fact]
    public void VoidDiscount_WithAnUnknownDiscountId_ReturnsNotFound()
    {
        var (controller, _) = CreateController(storedDiscount: null);

        IActionResult result = controller.VoidDiscount(PlayerId, DiscountId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void VoidDiscount_WithAPlayerIdOfZero_ReturnsBadRequest()
    {
        var (controller, _) = CreateController(CancelledDiscount());

        IActionResult result = controller.VoidDiscount(0, DiscountId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void VoidDiscount_WithADiscountIdOfZero_ReturnsBadRequest()
    {
        var (controller, _) = CreateController(CancelledDiscount());

        IActionResult result = controller.VoidDiscount(PlayerId, 0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ===== CancelDiscount =====

    [Fact]
    public void CancelDiscount_WithExplicitId_ReturnsNoContent()
    {
        var (controller, discounts) = CreateController(CancelledDiscount());
        discounts.DeactivateResult = true;

        IActionResult result = controller.CancelDiscount(PlayerId, DiscountId);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public void CancelDiscount_WithAnUnknownId_ReturnsNotFound()
    {
        var (controller, discounts) = CreateController(CancelledDiscount());
        discounts.DeactivateResult = false;

        IActionResult result = controller.CancelDiscount(PlayerId, DiscountId);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public void CancelDiscount_WithAPlayerIdOfZero_ReturnsBadRequest()
    {
        var (controller, _) = CreateController();

        IActionResult result = controller.CancelDiscount(0, DiscountId);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ---- Fakes ----

    private class FakeDiscountDao : DiscountDao
    {
        private readonly Discount? _stored;
        public bool VoidCalled { get; private set; }
        public bool DeactivateResult { get; set; } = true;

        public FakeDiscountDao(Discount? stored = null) : base("") => _stored = stored;

        public override List<Discount> GetDiscountsByPlayer(long playerId)
            => _stored != null && _stored.PlayerId == playerId
                ? new List<Discount> { _stored }
                : new List<Discount>();

        public override bool VoidDiscount(long playerId, long discountId)
        {
            VoidCalled = true;
            return true;
        }

        public override bool DeactivateDiscount(long playerId, long discountId)
            => DeactivateResult;
    }

    private class FakeJugadoresDao : IJugadoresDao
    {
        public IReadOnlyList<JugadorResumen> ListarJugadores() => new List<JugadorResumen>();
        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => null;
    }

    private class FakePlayerDAO : PlayerDAO
    {
        public FakePlayerDAO() : base("") { }
    }

    private class FakeEnrollmentFeeDAO : EnrollmentFeeDAO
    {
        public FakeEnrollmentFeeDAO() : base("") { }
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
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria>
        {
            new() { IdCategoria = 1, Nombre = "AFA 20067" }
        };
    }
}
