namespace ApiGestion.Tests;

using System.Reflection;
using ApiGestion.Controllers;
using ApiGestion.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceLibrary;

// POST api/integracion/v1/jugadores/{id}/baja: el aviso de baja del Portal Deportivo (SCRUM-757).
public class IntegracionBajasControllerTests
{
    private static IntegracionBajasController Crear(Func<int, ResultadoOperacion<BajaAplicada>> servicio) =>
        new(new ServicioFalso(servicio), NullLogger<IntegracionBajasController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    [Fact]
    public void ConLaBajaAplicada_Responde200ConLoQueSeQuito_SinMontos()
    {
        var controlador = Crear(_ => ResultadoOperacion<BajaAplicada>.Ok(new BajaAplicada(3, 1)));

        var ok = Assert.IsType<OkObjectResult>(controlador.Baja(7));

        var cuerpo = Assert.IsType<BajaInformadaResponseDTO>(ok.Value);
        Assert.Equal(7, cuerpo.IdJugador);
        Assert.Equal(3, cuerpo.CuotasFuturasQuitadas);
        Assert.Equal(1, cuerpo.BeneficiosCancelados);
        Assert.DoesNotContain(typeof(BajaInformadaResponseDTO).GetProperties(), p => p.PropertyType == typeof(decimal));
    }

    [Fact]
    public void UnJugadorQueNoExiste_Responde404()
    {
        var controlador = Crear(id => ResultadoOperacion<BajaAplicada>.Falla(TipoResultado.NoEncontrado, $"No existe el jugador {id}."));

        var resultado = Assert.IsAssignableFrom<ObjectResult>(controlador.Baja(999));

        Assert.Equal(404, resultado.StatusCode);
    }

    [Fact]
    public void UnaBajaConFechaInvalida_Responde422()
    {
        var controlador = Crear(_ => ResultadoOperacion<BajaAplicada>.Falla(TipoResultado.NoProcesable, "fecha"));

        Assert.Equal(422, Assert.IsAssignableFrom<ObjectResult>(controlador.Baja(7)).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void UnIdInvalido_Responde400SinLlamarAlServicio(int id)
    {
        bool llamado = false;
        var controlador = Crear(_ => { llamado = true; return ResultadoOperacion<BajaAplicada>.Ok(new BajaAplicada(0, 0)); });

        Assert.Equal(400, Assert.IsAssignableFrom<ObjectResult>(controlador.Baja(id)).StatusCode);
        Assert.False(llamado);
    }

    [Fact]
    public void SiElServicioFalla_Responde500_YNoSeFiltraElDetalle()
    {
        var controlador = Crear(_ => throw new InvalidOperationException("detalle interno secreto"));

        var resultado = Assert.IsAssignableFrom<ObjectResult>(controlador.Baja(7));

        Assert.Equal(500, resultado.StatusCode);
        Assert.DoesNotContain("secreto", System.Text.Json.JsonSerializer.Serialize(resultado.Value));
    }

    [Fact]
    public void LaRutaYLosPermisos_SonLosDeLaIntegracionDelDeportivo()
    {
        var tipo = typeof(IntegracionBajasController);
        var accion = tipo.GetMethod(nameof(IntegracionBajasController.Baja))!;

        Assert.Equal("api/integracion/v1/jugadores", tipo.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("{idJugador:int}/baja", accion.GetCustomAttribute<HttpPostAttribute>()!.Template);
        // Los mismos roles que el aviso de cargos de alta.
        Assert.Equal(
            typeof(IntegracionJugadoresController).GetCustomAttribute<AuthorizeAttribute>()!.Roles,
            tipo.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    private sealed class ServicioFalso(Func<int, ResultadoOperacion<BajaAplicada>> respuesta) : IJugadoresBajaService
    {
        public ResultadoOperacion<BajaAplicada> AplicarBajaInformada(int idJugador) => respuesta(idJugador);
    }
}
