namespace ApiGestion.Tests;

using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiGestion.Controllers;
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
        return (new PagosController(pagosService), pagosService);
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

    // ---- Fakes ----

    private class FakePagosService : IPagosService
    {
        public ResumenPagos ResumenResult { get; set; } = new();

        public ResumenPagos ObtenerResumen() => ResumenResult;

        // El controller bajo prueba solo llama a ObtenerResumen; nada más debería invocarse.
        public RegistrarPagoResultado RegistrarPago(RegistrarPagoRequest request) => throw new NotSupportedException();
        public CobrarPagosPendientesResultado CobrarPagosPendientes(CobrarPagosPendientesRequest request) => throw new NotSupportedException();
        public IReadOnlyList<PendienteJugador> ObtenerPendientes() => throw new NotSupportedException();
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => throw new NotSupportedException();
    }
}
