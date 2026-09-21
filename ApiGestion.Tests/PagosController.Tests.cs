namespace ApiGestion.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ApiGestion.Controllers;
using ApiGestion.Models;
using EntityLibrary;
using ServiceLibrary;

// Reglas de PagosController.ObtenerResumen (HU-019): el endpoint es un passthrough de
// IPagosService.ObtenerResumen, así que lo único que le corresponde a esta capa es exponer
// tal cual lo que devuelve la capa de negocio (incluidos los campos nuevos de deuda global) y
// exigir autenticación. La fórmula de deuda_global_total / jugadores_morosos vive en SQL
// (PagosDao.ObtenerResumen) y se validó aparte contra datos reales, no acá.
public class PagosControllerTests
{
    private static (PagosController controller, FakePagosService pagosService) CreateController()
    {
        FakePagosService pagosService = new();
        return (new PagosController(NullLogger<PagosController>.Instance, pagosService), pagosService);
    }

    private static PlayerAccount Account(int id, string lastName, decimal amountOwed, int installments = 1)
    {
        return new PlayerAccount
        {
            PlayerId = id,
            FirstName = "MATIAS",
            LastName = lastName,
            Dni = $"9900000{id}",
            Category = "AFA 20067",
            AmountOwed = amountOwed,
            PendingInstallments = installments
        };
    }

    [Fact]
    public void GetResumen_ReturnsOkWithTheServiceResultUnchanged()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.ResumenResult = new ResumenPagos
        {
            RecaudadoAnioActual = 1_200_000m,
            PagosDelMes = 8,
            CantidadPendientes = 5,
            DeudaGlobalTotal = 1_480_000m,
            JugadoresMorosos = 32
        };

        IActionResult result = controller.ObtenerResumen();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(pagosService.ResumenResult, ok.Value);
    }

    [Fact]
    public void GetResumen_ExposesDeudaGlobalTotalAndJugadoresMorosos()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.ResumenResult = new ResumenPagos { DeudaGlobalTotal = 255_000m, JugadoresMorosos = 3 };

        IActionResult result = controller.ObtenerResumen();

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        ResumenPagos resumen = Assert.IsType<ResumenPagos>(ok.Value);
        Assert.Equal(255_000m, resumen.DeudaGlobalTotal);
        Assert.Equal(3, resumen.JugadoresMorosos);
    }

    [Fact]
    public void Controller_RequiresAnAuthenticatedUser()
    {
        Assert.NotNull(typeof(PagosController).GetCustomAttribute<AuthorizeAttribute>());
    }

    // ---- Player accounts (HU-029) ----
    // The debt rule lives in SQL (PagosDao.GetPlayerAccounts) and is checked against the real
    // database by the Postman collection "HU-029 - Filtro de Jugadores Deudores". Here only the
    // contract of the endpoint is covered: what it asks the service, what it returns and how it fails.

    [Fact]
    public void GetPlayerAccounts_WithoutFilter_AsksTheServiceForEveryPlayer()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();

        controller.GetPlayerAccounts();

        Assert.Equal(new[] { false }, pagosService.PlayerAccountsCalls);
    }

    [Fact]
    public void GetPlayerAccounts_WithOnlyDebtors_AsksTheServiceForDebtorsOnly()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();

        controller.GetPlayerAccounts(onlyDebtors: true);

        Assert.Equal(new[] { true }, pagosService.PlayerAccountsCalls);
    }

    [Fact]
    public void GetPlayerAccounts_ReturnsTheRowsMappedToTheResponseContract()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.PlayerAccountsResult = new List<PlayerAccount>
        {
            Account(1, "SANCHEZ", 255_000m, installments: 3),
            Account(2, "CORREAS", 85_000m)
        };

        IActionResult result = controller.GetPlayerAccounts(onlyDebtors: true);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        List<PlayerAccountResponseDTO> rows = Assert.IsType<List<PlayerAccountResponseDTO>>(ok.Value);
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].PlayerId);
        Assert.Equal("MATIAS", rows[0].FirstName);
        Assert.Equal("SANCHEZ", rows[0].LastName);
        Assert.Equal("99000001", rows[0].Dni);
        Assert.Equal("AFA 20067", rows[0].Category);
        Assert.Equal(255_000m, rows[0].AmountOwed);
        Assert.Equal(3, rows[0].PendingInstallments);
        Assert.Equal(85_000m, rows[1].AmountOwed);
    }

    [Fact]
    public void GetPlayerAccounts_KeepsTheOrderTheServiceReturns()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.PlayerAccountsResult = new List<PlayerAccount>
        {
            Account(7, "ZAPATA", 300_000m),
            Account(3, "ABAD", 100_000m)
        };

        OkObjectResult ok = Assert.IsType<OkObjectResult>(controller.GetPlayerAccounts(onlyDebtors: true));

        List<PlayerAccountResponseDTO> rows = Assert.IsType<List<PlayerAccountResponseDTO>>(ok.Value);
        Assert.Equal(new[] { 7, 3 }, rows.Select(row => row.PlayerId));
    }

    [Fact]
    public void GetPlayerAccounts_WithNoDebtors_ReturnsOkWithAnEmptyList()
    {
        (PagosController controller, _) = CreateController();

        IActionResult result = controller.GetPlayerAccounts(onlyDebtors: true);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<List<PlayerAccountResponseDTO>>(ok.Value));
    }

    [Fact]
    public void GetPlayerAccounts_WhenTheServiceFails_ReturnsA500WithAClearMessage()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.PlayerAccountsError = new InvalidOperationException("connection lost");

        IActionResult result = controller.GetPlayerAccounts(onlyDebtors: true);

        ObjectResult error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal(false, error.Value?.GetType().GetProperty("exito")?.GetValue(error.Value));
        Assert.Equal("Error interno al obtener los jugadores.", error.Value?.GetType().GetProperty("mensaje")?.GetValue(error.Value));
    }

    [Fact]
    public void GetPlayerAccounts_IsAReadOnlyGetEndpoint()
    {
        MethodInfo method = typeof(PagosController).GetMethod(nameof(PagosController.GetPlayerAccounts))!;

        HttpGetAttribute get = Assert.Single(method.GetCustomAttributes<HttpGetAttribute>());
        Assert.Equal("player-accounts", get.Template);
        Assert.Empty(method.GetCustomAttributes<HttpPostAttribute>());
        Assert.Empty(method.GetCustomAttributes<HttpPutAttribute>());
        Assert.Empty(method.GetCustomAttributes<HttpDeleteAttribute>());
    }

    // ---- Fakes ----

    private class FakePagosService : IPagosService
    {
        public ResumenPagos ResumenResult { get; set; } = new();
        public IReadOnlyList<PlayerAccount> PlayerAccountsResult { get; set; } = new List<PlayerAccount>();
        public Exception? PlayerAccountsError { get; set; }
        public List<bool> PlayerAccountsCalls { get; } = new();

        public ResumenPagos ObtenerResumen() => ResumenResult;

        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors)
        {
            PlayerAccountsCalls.Add(onlyDebtors);

            if (PlayerAccountsError != null)
            {
                throw PlayerAccountsError;
            }

            return PlayerAccountsResult;
        }

        // El controller bajo prueba solo llama a ObtenerResumen; nada más debería invocarse.
        public RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request) => throw new NotSupportedException();
        public CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request) => throw new NotSupportedException();
        public IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null) => throw new NotSupportedException();
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => throw new NotSupportedException();
    }
}
