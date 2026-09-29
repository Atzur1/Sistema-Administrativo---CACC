namespace ApiGestion.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Controllers;
using ApiGestion.Models;
using DaoLibrary;
using EntityLibrary;

// HU-041: AuditoriaController es un passthrough de IAuditDao.Search con validación
// de parámetros propia. Los filtros SQL y el orden DESC se validaron contra la base
// real durante QA (29/09/2026). Acá se cubre: autorización, cada rama de 400, el
// happy path y el 500 cuando el DAO falla.
public class AuditoriaControllerTests
{
    private static (AuditoriaController controller, FakeAuditDao dao) CreateController()
    {
        FakeAuditDao dao = new();
        return (new AuditoriaController(dao), dao);
    }

    private static AuditEvent MakeEvent(long id = 1) => new()
    {
        Id = id,
        Entity = "PAGOS",
        EntityId = id.ToString(),
        Action = "INSERT",
        TimestampUtc = DateTime.UtcNow,
    };

    // ── Autorización ────────────────────────────────────────────────────────

    [Fact]
    public void Controller_RequiresRoleAdmin()
    {
        AuthorizeAttribute? attr = typeof(AuditoriaController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(attr);
        Assert.Equal("1", attr.Roles);
    }

    [Fact]
    public void BuscarCambios_IsAGetEndpointUnderCambios()
    {
        MethodInfo method = typeof(AuditoriaController)
            .GetMethod(nameof(AuditoriaController.BuscarCambios))!;

        HttpGetAttribute get = Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
        Assert.Equal("cambios", get.Template);
    }

    // ── Validación de parámetros → 400 ──────────────────────────────────────

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 101)]
    public void BuscarCambios_WithInvalidPagination_ReturnsBadRequest(int pagina, int tamano)
    {
        (AuditoriaController controller, _) = CreateController();

        IActionResult result = controller.BuscarCambios(pagina: pagina, tamanoPagina: tamano);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BuscarCambios_WithNonPositiveUserId_ReturnsBadRequest()
    {
        (AuditoriaController controller, _) = CreateController();

        IActionResult result = controller.BuscarCambios(idUsuario: 0);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BuscarCambios_WithInvertedDateRange_ReturnsBadRequest()
    {
        (AuditoriaController controller, _) = CreateController();
        DateTime desde = new(2026, 9, 29, 23, 0, 0, DateTimeKind.Utc);
        DateTime hasta = new(2026, 9, 29, 1, 0, 0, DateTimeKind.Utc);

        IActionResult result = controller.BuscarCambios(desdeUtc: desde, hastaUtc: hasta);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BuscarCambios_WithEqualDates_ReturnsBadRequest()
    {
        (AuditoriaController controller, _) = CreateController();
        DateTime momento = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

        IActionResult result = controller.BuscarCambios(desdeUtc: momento, hastaUtc: momento);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BuscarCambios_WithInvalidAction_ReturnsBadRequest()
    {
        (AuditoriaController controller, _) = CreateController();

        IActionResult result = controller.BuscarCambios(accion: "BORRAR");

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BuscarCambios_WithTooLongEntity_ReturnsBadRequest()
    {
        (AuditoriaController controller, _) = CreateController();
        string entidadLarga = new('X', 129);

        IActionResult result = controller.BuscarCambios(entidad: entidadLarga);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public void BuscarCambios_WithTooLongEmail_ReturnsBadRequest()
    {
        (AuditoriaController controller, _) = CreateController();
        string emailLargo = new('a', 255);

        IActionResult result = controller.BuscarCambios(emailUsuario: emailLargo);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public void BuscarCambios_WithNoFilters_ReturnsOkWithDaoResults()
    {
        (AuditoriaController controller, FakeAuditDao dao) = CreateController();
        dao.Items = new List<AuditEvent> { MakeEvent(1), MakeEvent(2) };
        dao.Total = 2;

        IActionResult result = controller.BuscarCambios();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        AuditSearchResponseDto response = Assert.IsType<AuditSearchResponseDto>(ok.Value);
        Assert.Equal(2, response.Total);
        Assert.Equal(2, response.Items.Count);
    }

    [Fact]
    public void BuscarCambios_WithEmptyResults_ReturnsOkWithZeroTotal()
    {
        (AuditoriaController controller, _) = CreateController();

        IActionResult result = controller.BuscarCambios();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        AuditSearchResponseDto response = Assert.IsType<AuditSearchResponseDto>(ok.Value);
        Assert.Equal(0, response.Total);
        Assert.Empty(response.Items);
    }

    [Fact]
    public void BuscarCambios_PassesPaginationParametersThroughToDao()
    {
        (AuditoriaController controller, FakeAuditDao dao) = CreateController();

        controller.BuscarCambios(pagina: 3, tamanoPagina: 25);

        Assert.Equal(3, dao.LastPage);
        Assert.Equal(25, dao.LastPageSize);
    }

    [Fact]
    public void BuscarCambios_WithValidActionLowercase_NormalizesAndCallsDao()
    {
        (AuditoriaController controller, FakeAuditDao dao) = CreateController();

        IActionResult result = controller.BuscarCambios(accion: "update");

        Assert.IsType<OkObjectResult>(result);
        Assert.Equal("UPDATE", dao.LastAction);
    }

    [Fact]
    public void BuscarCambios_ReturnsPageAndPageSizeFromDaoCall()
    {
        (AuditoriaController controller, FakeAuditDao dao) = CreateController();
        dao.Total = 5;

        OkObjectResult ok = Assert.IsType<OkObjectResult>(controller.BuscarCambios(pagina: 2, tamanoPagina: 10));

        AuditSearchResponseDto response = Assert.IsType<AuditSearchResponseDto>(ok.Value);
        Assert.Equal(2, response.Page);
        Assert.Equal(10, response.PageSize);
    }

    // ── Error del DAO → 500 ─────────────────────────────────────────────────

    [Fact]
    public void BuscarCambios_WhenDaoThrows_Returns500WithClearMessage()
    {
        (AuditoriaController controller, FakeAuditDao dao) = CreateController();
        dao.Error = new InvalidOperationException("connection lost");

        IActionResult result = controller.BuscarCambios();

        ObjectResult error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        string? mensaje = error.Value?.GetType().GetProperty("mensaje")?.GetValue(error.Value) as string;
        Assert.False(string.IsNullOrWhiteSpace(mensaje));
    }

    // ── Fake ────────────────────────────────────────────────────────────────

    private class FakeAuditDao : IAuditDao
    {
        public IReadOnlyList<AuditEvent> Items { get; set; } = new List<AuditEvent>();
        public long Total { get; set; }
        public Exception? Error { get; set; }
        public int LastPage { get; private set; }
        public int LastPageSize { get; private set; }
        public string? LastAction { get; private set; }

        public (IReadOnlyList<AuditEvent> Items, long Total) Search(
            DateTime? fromUtc, DateTime? toUtc, int? userId, string? userEmail,
            string? entity, string? action, int page, int pageSize)
        {
            LastPage = page;
            LastPageSize = pageSize;
            LastAction = action;
            if (Error is not null) throw Error;
            return (Items, Total);
        }
    }
}
