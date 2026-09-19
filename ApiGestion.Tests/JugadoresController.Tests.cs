namespace ApiGestion.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using ApiGestion.Controllers;
using DaoLibrary;
using EntityLibrary;
using Microsoft.Data.SqlClient;

// Rules of JugadoresController.ObtenerHistorialPagos (HU-017): player lookup, page and
// page-size normalization, error handling and the read-only contract of the endpoint.
// The DAOs are hand-written fakes, so nothing here touches the database. The SQL grouping
// and ordering of PagosDao.ObtenerHistorialPagos need a real database and are covered by
// the Postman collection "HU-017 - Historial de Pagos por Jugador".
public class JugadoresControllerTests
{
    private const int KnownPlayerId = 3;

    private static readonly JugadorResumen KnownPlayer = new()
    {
        IdJugador = KnownPlayerId,
        Nombre = "MATIAS",
        Apellido = "PRUEBA",
        Dni = "99000003",
        Categoria = "AFA 20067",
        Genero = "Masculino"
    };

    private static (JugadoresController controller, FakePagosDao pagosDao) CreateController(
        JugadorResumen? player = null, Exception? playerLookupError = null, Exception? historyError = null)
    {
        FakePagosDao pagosDao = new() { HistoryError = historyError };
        FakeJugadoresDao jugadoresDao = new() { Player = player, LookupError = playerLookupError };

        return (new JugadoresController(jugadoresDao, pagosDao), pagosDao);
    }

    private static object? ReadProperty(object? value, string name)
    {
        return value?.GetType().GetProperty(name)?.GetValue(value);
    }

    // ---- Player lookup ----

    [Fact]
    public void GetPaymentHistory_UnknownPlayer_ReturnsNotFound()
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(player: null);

        IActionResult result = controller.ObtenerHistorialPagos(999);

        NotFoundObjectResult notFound = Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal(false, ReadProperty(notFound.Value, "exito"));
        Assert.Equal("Jugador no encontrado.", ReadProperty(notFound.Value, "mensaje"));
        Assert.Empty(pagosDao.HistoryCalls);
    }

    [Fact]
    public void GetPaymentHistory_KnownPlayer_ReturnsOkWithTheHistoryFromTheDao()
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(KnownPlayer);

        IActionResult result = controller.ObtenerHistorialPagos(KnownPlayerId);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(pagosDao.HistoryResult, ok.Value);
    }

    [Fact]
    public void GetPaymentHistory_KnownPlayer_QueriesTheHistoryOfThatPlayer()
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(KnownPlayer);

        controller.ObtenerHistorialPagos(KnownPlayerId);

        HistoryCall call = Assert.Single(pagosDao.HistoryCalls);
        Assert.Equal(KnownPlayerId, call.PlayerId);
    }

    // ---- Pagination ----

    [Fact]
    public void GetPaymentHistory_WithoutParameters_UsesFirstPageOfTenRecords()
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(KnownPlayer);

        controller.ObtenerHistorialPagos(KnownPlayerId);

        HistoryCall call = Assert.Single(pagosDao.HistoryCalls);
        Assert.Equal(1, call.Page);
        Assert.Equal(10, call.PageSize);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(-50, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(37, 37)]
    public void GetPaymentHistory_Page_IsNeverLowerThanOne(int requestedPage, int expectedPage)
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(KnownPlayer);

        controller.ObtenerHistorialPagos(KnownPlayerId, requestedPage, 10);

        Assert.Equal(expectedPage, Assert.Single(pagosDao.HistoryCalls).Page);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-5, 10)]
    [InlineData(101, 10)]
    [InlineData(500, 10)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(25, 25)]
    [InlineData(100, 100)]
    public void GetPaymentHistory_PageSize_OutsideOneToOneHundredFallsBackToTen(int requestedPageSize, int expectedPageSize)
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(KnownPlayer);

        controller.ObtenerHistorialPagos(KnownPlayerId, 1, requestedPageSize);

        Assert.Equal(expectedPageSize, Assert.Single(pagosDao.HistoryCalls).PageSize);
    }

    // ---- Error handling ----

    [Fact]
    public void GetPaymentHistory_HistoryQueryFails_ReturnsServerError()
    {
        (JugadoresController controller, _) = CreateController(KnownPlayer, historyError: new InvalidOperationException("db down"));

        IActionResult result = controller.ObtenerHistorialPagos(KnownPlayerId);

        ObjectResult error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal(false, ReadProperty(error.Value, "exito"));
        Assert.Equal("Error interno al obtener el historial de pagos.", ReadProperty(error.Value, "mensaje"));
    }

    [Fact]
    public void GetPaymentHistory_PlayerLookupFails_ReturnsServerErrorWithoutQueryingHistory()
    {
        (JugadoresController controller, FakePagosDao pagosDao) = CreateController(playerLookupError: new InvalidOperationException("db down"));

        IActionResult result = controller.ObtenerHistorialPagos(KnownPlayerId);

        ObjectResult error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        Assert.Empty(pagosDao.HistoryCalls);
    }

    // ---- Access and read-only contract ----

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(JugadoresController).GetCustomAttribute<AuthorizeAttribute>());
    }

    [Fact]
    public void PaymentHistoryEndpoint_ExposesOnlyAGetAction()
    {
        // Read-only view (HU-017): no action bound to the history route may create, edit or delete payments.
        const string historyRoute = "{id}/historial-pagos";

        List<string> verbs = typeof(JugadoresController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Where(attribute => attribute.Template == historyRoute)
            .SelectMany(attribute => attribute.HttpMethods)
            .ToList();

        Assert.Equal(new[] { "GET" }, verbs);
    }

    // ---- Fakes ----

    private record HistoryCall(int PlayerId, int Page, int PageSize);

    private class FakeJugadoresDao : IJugadoresDao
    {
        public JugadorResumen? Player { get; init; }
        public Exception? LookupError { get; init; }

        public IReadOnlyList<JugadorResumen> ListarJugadores() => throw new NotSupportedException();

        public JugadorResumen? ObtenerJugadorPorId(int idJugador)
        {
            if (LookupError != null)
            {
                throw LookupError;
            }

            return Player;
        }
    }

    private class FakePagosDao : IPagosDao
    {
        public List<HistoryCall> HistoryCalls { get; } = new();
        public Exception? HistoryError { get; init; }
        public HistorialPagosResultado HistoryResult { get; } = new() { Total = 1, Page = 1, PageSize = 10 };

        public HistorialPagosResultado ObtenerHistorialPagos(int idJugador, int page, int pageSize)
        {
            HistoryCalls.Add(new HistoryCall(idJugador, page, pageSize));

            if (HistoryError != null)
            {
                throw HistoryError;
            }

            return HistoryResult;
        }

        // The controller under test only reads the history, so nothing else may be called.
        public Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio) => throw new NotSupportedException();
        public Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio) => throw new NotSupportedException();
        public DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, DateTime fechaVencimiento) => throw new NotSupportedException();
        public void ActualizarSaldoPendiente(SqlConnection conexion, SqlTransaction transaccion, int idPago, decimal nuevoMonto) => throw new NotSupportedException();
        public void EliminarPago(SqlConnection conexion, SqlTransaction transaccion, int idPago) => throw new NotSupportedException();
        public int InsertarPago(SqlConnection conexion, SqlTransaction transaccion, Pago pago) => throw new NotSupportedException();
        public IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago) => throw new NotSupportedException();
        public void MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago) => throw new NotSupportedException();
        public IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados() => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => throw new NotSupportedException();
        public ResumenPagos ObtenerResumen() => throw new NotSupportedException();
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => throw new NotSupportedException();
        public void GenerarCuotasPendientesDelMes(string genero, int mes, int anio) => throw new NotSupportedException();
    }
}
