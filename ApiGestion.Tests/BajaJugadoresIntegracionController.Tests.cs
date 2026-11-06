namespace ApiGestion.Tests;

using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using ApiGestion.Controllers;
using ApiGestion.Models;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceLibrary;

// HU-057: el contrato HTTP de la baja y la reincorporación que informa el Portal Deportivo. El servicio es falso: acá se prueba
// solo cómo responde el controller (código, errorCode, field) y que la respuesta nunca lleve montos ni cantidades de cuotas.
public class BajaJugadoresIntegracionControllerTests
{
    private static readonly DateTime Baja = new(2026, 10, 15);

    [Fact]
    public void LaBaja_Responde200ConElEstadoYLaFecha_SinMontosNiCantidades()
    {
        var servicio = new ServicioFalso { Baja = ResultadoOperacion<BajaIntegracionRegistrada>.Ok(new(7, Baja, false)) };

        var ok = Assert.IsType<OkObjectResult>(Controlador().DarDeBaja(7, new BajaIntegracionRequestDTO { FechaBaja = "2026-10-15" }, servicio));

        Assert.Equal("2026-10-15", servicio.FechaRecibida);
        var dto = Assert.IsType<BajaIntegracionResponseDTO>(ok.Value);
        Assert.Equal(7, dto.IdJugador);
        Assert.Equal("baja", dto.Estado);
        Assert.Equal("2026-10-15", dto.FechaBaja);
        Assert.False(dto.YaEstabaDadoDeBaja);
        var campos = JsonDocument.Parse(JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n);
        Assert.Equal(new[] { "estado", "fechaBaja", "idJugador", "yaEstabaDadoDeBaja" }, campos);
    }

    [Fact]
    public void UnReintento_TambienEs200_YLoDice()
    {
        var servicio = new ServicioFalso { Baja = ResultadoOperacion<BajaIntegracionRegistrada>.Ok(new(7, Baja, true)) };

        var ok = Assert.IsType<OkObjectResult>(Controlador().DarDeBaja(7, new BajaIntegracionRequestDTO { FechaBaja = "2026-10-15" }, servicio));

        Assert.True(Assert.IsType<BajaIntegracionResponseDTO>(ok.Value).YaEstabaDadoDeBaja);
    }

    [Fact]
    public void SinCuerpo_ElServicioRecibeLaFechaVacia_YUnDatoInvalidoEs400ConElCampo()
    {
        var servicio = new ServicioFalso
        {
            Baja = ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.Invalido, "Indicá la fecha de baja.", "fechaBaja")
        };

        var resultado = Assert.IsAssignableFrom<ObjectResult>(Controlador().DarDeBaja(7, null, servicio));

        Assert.Null(servicio.FechaRecibida);
        Assert.Equal(400, resultado.StatusCode);
        var error = Error(resultado);
        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal("fechaBaja", error.Field);
    }

    [Theory]
    [InlineData(TipoResultado.NoEncontrado, 404, "NOT_FOUND")]
    [InlineData(TipoResultado.Conflicto, 409, "CONFLICT")]
    [InlineData(TipoResultado.NoProcesable, 422, "UNPROCESSABLE")]
    public void LasFallasDeNegocio_UsanElContratoComun_YSoloLos400LlevanField(TipoResultado tipo, int status, string codigo)
    {
        var servicio = new ServicioFalso { Baja = ResultadoOperacion<BajaIntegracionRegistrada>.Falla(tipo, "motivo", "fechaBaja") };

        var resultado = Assert.IsAssignableFrom<ObjectResult>(Controlador().DarDeBaja(7, new BajaIntegracionRequestDTO { FechaBaja = "2026-10-15" }, servicio));

        Assert.Equal(status, resultado.StatusCode);
        Assert.Equal(codigo, Error(resultado).ErrorCode);
        Assert.Null(Error(resultado).Field);
    }

    [Fact]
    public void UnaFallaInterna_Es500Controlado_SinDetalles()
    {
        var servicio = new ServicioFalso { Error = new InvalidOperationException("detalle interno de la base") };

        var resultado = Assert.IsAssignableFrom<ObjectResult>(Controlador().DarDeBaja(7, new BajaIntegracionRequestDTO { FechaBaja = "2026-10-15" }, servicio));

        Assert.Equal(500, resultado.StatusCode);
        Assert.Equal("INTERNAL_ERROR", Error(resultado).ErrorCode);
        Assert.DoesNotContain("detalle interno", JsonSerializer.Serialize(resultado.Value));
    }

    [Fact]
    public void LaReincorporacion_Responde200ConLaFechaDeAlta_YPasaLaCategoria()
    {
        var servicio = new ServicioFalso
        {
            Reingreso = ResultadoOperacion<ReincorporacionRegistrada>.Ok(new(7, new DateTime(2026, 11, 3), true))
        };

        var ok = Assert.IsType<OkObjectResult>(Controlador().Reincorporar(7, new ReincorporacionIntegracionRequestDTO { IdCategoria = 4 }, servicio));

        Assert.Equal(4, servicio.CategoriaRecibida);
        var dto = Assert.IsType<ReincorporacionIntegracionResponseDTO>(ok.Value);
        Assert.Equal("2026-11-03", dto.FechaAlta);
        Assert.True(dto.CategoriaCambiada);
    }

    [Fact]
    public void LaReincorporacionSinCuerpo_ConservaLaCategoria_YUnJugadorActivoEs409()
    {
        var servicio = new ServicioFalso
        {
            Reingreso = ResultadoOperacion<ReincorporacionRegistrada>.Falla(TipoResultado.Conflicto, "El jugador ya está activo.")
        };

        var resultado = Assert.IsAssignableFrom<ObjectResult>(Controlador().Reincorporar(7, null, servicio));

        Assert.Null(servicio.CategoriaRecibida);
        Assert.Equal(409, resultado.StatusCode);
        Assert.Equal("CONFLICT", Error(resultado).ErrorCode);
    }

    // La credencial de sistema (HU-055) la exige el middleware para toda /api/integracion; además hace falta el token de un rol
    // habilitado: los mismos que cargos-alta (SuperAdmin del Administrativo y Administrador (Tesorero)).
    [Theory]
    [InlineData(nameof(IntegracionJugadoresController.DarDeBaja), "{idJugador:int}/baja")]
    [InlineData(nameof(IntegracionJugadoresController.Reincorporar), "{idJugador:int}/reincorporacion")]
    public void LasRutasSonDeIntegracion_YExigenLosRolesDeCargosDeAlta(string accion, string plantilla)
    {
        var tipo = typeof(IntegracionJugadoresController);
        var metodo = tipo.GetMethod(accion)!;

        Assert.Equal("api/integracion/v1/jugadores", tipo.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(plantilla, metodo.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Null(metodo.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(metodo.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal("1,Administrador (Tesorero)", tipo.GetCustomAttribute<AuthorizeAttribute>()!.Roles);
    }

    private static IntegracionJugadoresController Controlador()
    {
        var controlador = new IntegracionJugadoresController(new AltaNoUsada(), NullLogger<IntegracionJugadoresController>.Instance);
        controlador.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Administrador (Tesorero)") }, "Bearer"))
            }
        };
        return controlador;
    }

    private static IntegrationErrorResponseDTO Error(ObjectResult resultado) => Assert.IsType<IntegrationErrorResponseDTO>(resultado.Value);

    private sealed class ServicioFalso : IJugadoresBajaService
    {
        public ResultadoOperacion<BajaIntegracionRegistrada>? Baja { get; init; }
        public ResultadoOperacion<ReincorporacionRegistrada>? Reingreso { get; init; }
        public Exception? Error { get; init; }
        public string? FechaRecibida { get; private set; }
        public int? CategoriaRecibida { get; private set; }

        public ResultadoOperacion<BajaIntegracionRegistrada> DarDeBaja(int idJugador, string? fechaBaja)
        {
            FechaRecibida = fechaBaja;
            if (Error != null) throw Error;
            return Baja!;
        }

        public ResultadoOperacion<ReincorporacionRegistrada> Reincorporar(int idJugador, int? idCategoria)
        {
            CategoriaRecibida = idCategoria;
            if (Error != null) throw Error;
            return Reingreso!;
        }
    }

    private sealed class AltaNoUsada : IJugadoresAltaService
    {
        public ResultadoOperacion<AltaJugadorRegistrada> Registrar(SolicitudAltaJugador solicitud) => throw new NotSupportedException();
        public ResultadoOperacion<CargosGenerados> GenerarCargosDeAlta(int idJugador) => throw new NotSupportedException();
        public ResultadoOperacion<AltaIntegracionRegistrada> AltaDesdeIntegracion(SolicitudAltaIntegracion solicitud, string? correlacion = null)
            => throw new NotSupportedException();
        public ResultadoOperacion<CambioCategoriaRealizado> CambiarCategoria(int idJugador, int? idCategoria, string? correlacion = null)
            => throw new NotSupportedException();
    }
}
