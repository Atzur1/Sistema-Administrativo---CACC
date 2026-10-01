namespace ApiGestion.Tests;

using System.Reflection;
using System.Security.Claims;
using DaoLibrary.Exceptions;
using Microsoft.AspNetCore.Http;
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

    // ---- Pendientes / deuda-por-categoria (HU-020) ----
    // El filtro idCategoria en sí (JOIN contra CATEGORIAS, sin bloqueos, etc.) se validó con
    // la API real (QA, 23-24/09); acá solo se cubre que el controller pasa el parámetro tal
    // cual al service, sin transformarlo ni perderlo.

    [Fact]
    public void GetPendientes_WithoutACategoryFilter_AsksTheServiceForEveryDebtor()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();

        controller.ObtenerPendientes();

        Assert.Equal(new int?[] { null }, pagosService.PendientesCalls);
    }

    [Fact]
    public void GetPendientes_WithACategoryFilter_PassesTheIdCategoriaThroughUnchanged()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();

        controller.ObtenerPendientes(idCategoria: 5);

        Assert.Equal(new int?[] { 5 }, pagosService.PendientesCalls);
    }

    [Fact]
    public void GetPendientes_ReturnsOkWithTheServiceResultUnchanged()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.PendientesResult = new List<PendienteJugador>
        {
            new() { IdJugador = 1, NombreCompleto = "PRUEBA, SANTIAGO", Dni = "99000001", IdCategoria = 1, Categoria = "AFA 20067", MontoTotal = 92_000m, CantidadCuotas = 1 },
        };

        IActionResult result = controller.ObtenerPendientes(idCategoria: 1);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(pagosService.PendientesResult, ok.Value);
    }

    [Fact]
    public void GetDeudaPorCategoria_DefaultsToTheCurrentYearWhenNoneIsGiven()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();

        controller.ObtenerDeudaPorCategoria();

        Assert.Equal(new[] { (DateTime.Now.Year, (int?)null) }, pagosService.DeudaPorCategoriaCalls);
    }

    [Fact]
    public void GetDeudaPorCategoria_PassesTheGivenAnioAndMesThrough()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();

        controller.ObtenerDeudaPorCategoria(anio: 2025, mes: 9);

        Assert.Equal(new[] { (2025, (int?)9) }, pagosService.DeudaPorCategoriaCalls);
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

    // ---- Cobro de cuotas (HU-025) ----
    // Las reglas de negocio viven en PagosService (ver PagosService.Cobro.Tests.cs). Acá se fija
    // lo que le toca al controller: tomar el operador del JWT (nunca del body), traducir los
    // errores a códigos HTTP y devolver el detalle que el frontend necesita para refrescar.

    private static void Autenticar(PagosController controller, string? idUsuario)
    {
        var claims = idUsuario == null ? Array.Empty<Claim>() : new[] { new Claim("idUsuario", idUsuario) };
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
    }

    private static RegistrarCobroRequestDto CobroBody() => new()
    {
        IdJugador = 12,
        IdsPago = new List<int> { 10, 11 },
        MetodoPago = "Transferencia"
    };

    [Fact]
    public void Cobro_TomaElOperadorDelTokenYPasaLaSolicitudAlServicio()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        Autenticar(controller, "7");

        controller.CobrarPagosPendientes(CobroBody());

        CobrarPagosPendientesRequest enviado = Assert.Single(pagosService.CobroCalls);
        Assert.Equal(7, enviado.IdUsuarioRegistro);
        Assert.Equal(12, enviado.IdJugador);
        Assert.Equal(new[] { 10, 11 }, enviado.IdsPago);
        Assert.Equal("Transferencia", enviado.MetodoPago);
    }

    [Fact]
    public void Cobro_SinOperadorEnElToken_Devuelve401YNoCobra()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        Autenticar(controller, null);

        IActionResult result = controller.CobrarPagosPendientes(CobroBody());

        Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Empty(pagosService.CobroCalls);
    }

    [Fact]
    public void Cobro_RechazoDeNegocio_Devuelve400ConElMensaje()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        Autenticar(controller, "7");
        pagosService.CobroError = new CobroInvalidoException("Las siguientes cuotas ya fueron abonadas: 11.");

        IActionResult result = controller.CobrarPagosPendientes(CobroBody());

        BadRequestObjectResult bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.Contains("ya fueron abonadas", bad.Value!.GetType().GetProperty("mensaje")!.GetValue(bad.Value)!.ToString());
    }

    [Fact]
    public void Cobro_ErrorInesperado_Devuelve500SinDetallesInternos()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        Autenticar(controller, "7");
        pagosService.CobroError = new InvalidOperationException("timeout de SQL");

        IActionResult result = controller.CobrarPagosPendientes(CobroBody());

        ObjectResult error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, error.StatusCode);
        Assert.DoesNotContain("timeout", error.Value!.GetType().GetProperty("mensaje")!.GetValue(error.Value)!.ToString());
    }

    [Fact]
    public void Cobro_Exitoso_DevuelveCuotasTotalMetodoYHoraDeRegistro()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        Autenticar(controller, "7");
        var registro = new DateTime(2026, 9, 30, 18, 45, 12);
        pagosService.CobroResult = new CobrarPagosPendientesResultado
        {
            IdJugador = 12,
            PagosAbonados = new List<int> { 10, 11 },
            Cuotas = new List<CuotaCobrada>
            {
                new() { IdPago = 10, Periodo = "Marzo 2026", Monto = 85_000m, Estado = "Pagado" },
                new() { IdPago = 11, Periodo = "Abril 2026", Monto = 85_000m, Estado = "Pagado" }
            },
            MontoTotal = 170_000m,
            MetodoPago = "Transferencia",
            FechaPago = registro.Date,
            FechaHoraRegistro = registro
        };

        IActionResult result = controller.CobrarPagosPendientes(CobroBody());

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        object body = ok.Value!;
        object? Valor(string nombre) => body.GetType().GetProperty(nombre)!.GetValue(body);
        Assert.Equal(true, Valor("exito"));
        Assert.Equal(170_000m, Valor("montoTotal"));
        Assert.Equal("Transferencia", Valor("metodoPago"));
        Assert.Equal(registro, Valor("fechaHoraRegistro"));
        Assert.Equal("Pagado", Valor("estado"));
        Assert.Same(pagosService.CobroResult.Cuotas, Valor("cuotas"));
    }

    [Fact]
    public void GetCuotas_DevuelveElEstadoDeCuotasDelJugador()
    {
        (PagosController controller, FakePagosService pagosService) = CreateController();
        pagosService.CuotasResult = new List<CuotaJugador> { new() { IdPago = 10, Periodo = "Marzo 2026", Estado = "Vencido" } };

        IActionResult result = controller.ObtenerCuotasJugador(12);

        OkObjectResult ok = Assert.IsType<OkObjectResult>(result);
        Assert.Same(pagosService.CuotasResult, ok.Value);
        Assert.Equal(new[] { 12 }, pagosService.CuotasCalls);
    }

    private class FakePagosService : IPagosService
    {
        public ResumenPagos ResumenResult { get; set; } = new();
        public IReadOnlyList<PlayerAccount> PlayerAccountsResult { get; set; } = new List<PlayerAccount>();
        public Exception? PlayerAccountsError { get; set; }
        public List<bool> PlayerAccountsCalls { get; } = new();
        public IReadOnlyList<PendienteJugador> PendientesResult { get; set; } = new List<PendienteJugador>();
        public List<int?> PendientesCalls { get; } = new();
        public IReadOnlyList<CategoriaDeuda> DeudaPorCategoriaResult { get; set; } = new List<CategoriaDeuda>();
        public List<(int Anio, int? Mes)> DeudaPorCategoriaCalls { get; } = new();

        public ResumenPagos ObtenerResumen() => ResumenResult;

        public CobrarPagosPendientesResultado CobroResult { get; set; } = new();
        public Exception? CobroError { get; set; }
        public List<CobrarPagosPendientesRequest> CobroCalls { get; } = new();
        public IReadOnlyList<CuotaJugador> CuotasResult { get; set; } = new List<CuotaJugador>();
        public List<int> CuotasCalls { get; } = new();

        public CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request)
        {
            CobroCalls.Add(request);
            if (CobroError != null)
            {
                throw CobroError;
            }

            return CobroResult;
        }

        public IReadOnlyList<CuotaJugador> ObtenerCuotasJugador(int idJugador)
        {
            CuotasCalls.Add(idJugador);
            return CuotasResult;
        }

        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors)
        {
            PlayerAccountsCalls.Add(onlyDebtors);

            if (PlayerAccountsError != null)
            {
                throw PlayerAccountsError;
            }

            return PlayerAccountsResult;
        }

        public IReadOnlyList<PendienteJugador> ObtenerPendientes(int? idCategoria = null)
        {
            PendientesCalls.Add(idCategoria);
            return PendientesResult;
        }

        public IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null)
        {
            DeudaPorCategoriaCalls.Add((anio, mes));
            return DeudaPorCategoriaResult;
        }

        // El controller bajo prueba no llama a estos métodos; nada más debería invocarse.
        public RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request) => throw new NotSupportedException();
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerTodosLosPagos() => throw new NotSupportedException();
        public ResumenPagosHoy ObtenerResumenHoy() => throw new NotSupportedException();
    }
}
