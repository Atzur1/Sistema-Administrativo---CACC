namespace ApiGestion.Tests;

using System.Security.Claims;
using System.Reflection;
using ApiGestion.Controllers;
using ApiGestion.Models;
using EntityLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceLibrary;

public class AltaJugadoresIntegracionControllerTests
{
    [Fact]
    public void AltaNueva_Responde201ConElJugadorYLosIndicadoresDeCargos()
    {
        var servicio = new AltaFalsa
        {
            Alta = ResultadoOperacion<AltaIntegracionRegistrada>.Ok(new(42, true, true, true, false))
        };
        var controlador = Controlador(servicio);

        var resultado = Assert.IsType<ObjectResult>(controlador.AltaDesdeIntegracion(
            new AltaIntegracionRequestDTO { Dni = "12345678", IdCategoria = 3 }));

        Assert.Equal(StatusCodes.Status201Created, resultado.StatusCode);
        Assert.Equal("corr-alta", servicio.Correlacion);
        var cuerpo = Assert.IsType<AltaIntegracionResponseDTO>(resultado.Value);
        Assert.Equal(42, cuerpo.IdJugador);
        Assert.True(cuerpo.Creado);
        Assert.True(cuerpo.PersonaExistente);
        Assert.True(cuerpo.InscripcionGenerada);
        Assert.False(cuerpo.CuotaGenerada);
    }

    [Fact]
    public void AltaActivaIdempotente_Responde200()
    {
        var servicio = new AltaFalsa
        {
            Alta = ResultadoOperacion<AltaIntegracionRegistrada>.Ok(new(42, false, true, false, false))
        };

        var resultado = Assert.IsType<OkObjectResult>(Controlador(servicio).AltaDesdeIntegracion(new AltaIntegracionRequestDTO()));

        Assert.Equal(42, Assert.IsType<AltaIntegracionResponseDTO>(resultado.Value).IdJugador);
    }

    [Fact]
    public void CambiarCategoria_UsaPutConElContratoYResponde200()
    {
        var servicio = new AltaFalsa
        {
            Cambio = ResultadoOperacion<CambioCategoriaRealizado>.Ok(new(42, 4, true))
        };
        var action = typeof(IntegracionJugadoresController).GetMethod(nameof(IntegracionJugadoresController.CambiarCategoria))!;

        var resultado = Assert.IsType<OkObjectResult>(Controlador(servicio).CambiarCategoria(
            42, new CambioCategoriaIntegracionRequestDTO { IdCategoria = 4 }));

        Assert.Equal("{idJugador:int}/categoria", action.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal("corr-alta", servicio.Correlacion);
        Assert.Equal((42, 4), (servicio.IdJugador, servicio.IdCategoria));
        Assert.Equal(4, Assert.IsType<CambioCategoriaIntegracionResponseDTO>(resultado.Value).IdCategoria);
    }

    private static IntegracionJugadoresController Controlador(AltaFalsa servicio)
    {
        var controlador = new IntegracionJugadoresController(servicio, NullLogger<IntegracionJugadoresController>.Instance);
        controlador.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "1") }, "Bearer")),
                TraceIdentifier = "corr-alta"
            }
        };
        return controlador;
    }

    private sealed class AltaFalsa : IJugadoresAltaService
    {
        public ResultadoOperacion<AltaIntegracionRegistrada>? Alta { get; init; }
        public ResultadoOperacion<CambioCategoriaRealizado>? Cambio { get; init; }
        public string? Correlacion { get; private set; }
        public int IdJugador { get; private set; }
        public int? IdCategoria { get; private set; }

        public ResultadoOperacion<AltaJugadorRegistrada> Registrar(SolicitudAltaJugador solicitud) => throw new NotSupportedException();
        public ResultadoOperacion<CargosGenerados> GenerarCargosDeAlta(int idJugador) => throw new NotSupportedException();

        public ResultadoOperacion<AltaIntegracionRegistrada> AltaDesdeIntegracion(SolicitudAltaIntegracion solicitud, string? correlacion = null)
        {
            Correlacion = correlacion;
            return Alta!;
        }

        public ResultadoOperacion<CambioCategoriaRealizado> CambiarCategoria(int idJugador, int? idCategoria, string? correlacion = null)
        {
            IdJugador = idJugador;
            IdCategoria = idCategoria;
            Correlacion = correlacion;
            return Cambio!;
        }
    }
}
