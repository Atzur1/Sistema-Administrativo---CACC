namespace ApiGestion.Tests;

using System.Reflection;
using System.Security.Claims;
using ApiGestion.Controllers;
using ApiGestion.Models;
using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

// Estado de deuda para el Portal Deportivo: la regla de las cuotas vencidas, quién puede preguntar y qué ve cada rol.
// El SQL se prueba aparte, contra la base (EstadoDeudaDao.Integration.Tests.cs).
public class EstadoDeudaJugadorTests
{
    [Theory]
    [InlineData(0, EstadoDeportivo.Habilitado, true)]
    [InlineData(1, EstadoDeportivo.SoloEntrenamientos, true)]
    [InlineData(2, EstadoDeportivo.Inhabilitado, false)]
    [InlineData(7, EstadoDeportivo.Inhabilitado, false)]
    public void ElEstadoSale_DeLasCuotasVencidas(int vencidas, EstadoDeportivo esperado, bool acceso)
    {
        var estado = new EstadoDeudaJugador(1, "41000001", vencidas, 100_000m, null);

        Assert.Equal(esperado, estado.Estado);
        Assert.Equal(acceso, estado.AccesoPermitido);
    }

    [Fact]
    public void UnJugadorDadoDeBaja_NoTieneAcceso_AunqueNoDebaNada()
    {
        var estado = new EstadoDeudaJugador(1, "41000001", 0, 0m, new DateTime(2026, 10, 1));

        Assert.Equal(EstadoDeportivo.Baja, estado.Estado);
        Assert.False(estado.AccesoPermitido);
        Assert.False(estado.EnTermino);
    }

    [Fact]
    public void EnTermino_EsDeberAlgoSinNadaVencido()
    {
        Assert.True(new EstadoDeudaJugador(1, "41000001", 0, 85_000m, null).EnTermino);
        Assert.False(new EstadoDeudaJugador(1, "41000001", 0, 0m, null).EnTermino);
        Assert.False(new EstadoDeudaJugador(1, "41000001", 1, 85_000m, null).EnTermino);
    }
}

public class EstadoDeudaControllerTests
{
    private sealed class FakeEstadoDeudaDao : IEstadoDeudaDao
    {
        public List<EstadoDeudaJugador> Jugadores { get; } = new();
        public IReadOnlyCollection<string>? UltimosDni { get; private set; }
        public IReadOnlyCollection<int>? UltimasCategorias { get; private set; }
        public bool Llamado { get; private set; }
        public bool Falla { get; set; }

        public IReadOnlyList<EstadoDeudaJugador> ObtenerPorDni(IReadOnlyCollection<string> dnis, IReadOnlyCollection<int>? categoriasPermitidas)
        {
            if (Falla) throw new InvalidOperationException("base caída");
            Llamado = true;
            UltimosDni = dnis;
            UltimasCategorias = categoriasPermitidas;
            if (categoriasPermitidas is { Count: 0 }) return new List<EstadoDeudaJugador>(); // sin categorías no ve a nadie (como el DAO real)
            return Jugadores.Where(j => dnis.Contains(j.Dni)).ToList();
        }
    }

    private static (EstadoDeudaController Controller, FakeEstadoDeudaDao Dao) Crear(string rol = "Don QR", params int[] categorias)
    {
        var dao = new FakeEstadoDeudaDao();
        var controller = new EstadoDeudaController(dao, NullLogger<EstadoDeudaController>.Instance);
        var claims = new List<Claim> { new(ClaimTypes.Role, rol) };
        claims.AddRange(categorias.Select(c => new Claim("id_categoria", c.ToString())));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer")) }
        };
        return (controller, dao);
    }

    [Fact]
    public void PorDni_UnDniEscritoConPuntos_SeBuscaSoloConDigitos()
    {
        var (controller, dao) = Crear();
        dao.Jugadores.Add(new EstadoDeudaJugador(5, "41000001", 1, 170_000m, null));

        var ok = Assert.IsType<OkObjectResult>(controller.PorDni("41.000.001"));
        var dto = Assert.IsType<EstadoDeudaResponseDTO>(ok.Value);

        Assert.Equal(new[] { "41000001" }, dao.UltimosDni);
        Assert.Equal("solo_entrenamientos", dto.Estado);
        Assert.True(dto.AccesoPermitido);
    }

    // Contrato v1 (HU-028): el Deportivo recibe solo el estado. Si alguien agrega un dato financiero a la respuesta, esta prueba falla.
    [Fact]
    public void LaRespuestaNoTieneNingunDatoFinanciero()
    {
        var propiedades = typeof(EstadoDeudaResponseDTO).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        Assert.Equal(new[] { "AccesoPermitido", "Dni", "Estado", "FechaBaja", "IdJugador" }, propiedades);
    }

    [Fact]
    public void LaRespuestaSerializadaNoDejaVerLaDeudaDeNadie()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            EstadoDeudaResponseDTO.FromEntity(new EstadoDeudaJugador(5, "41000001", 2, 255_000m, null)),
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Contains("\"estado\":\"inhabilitado\"", json);
        Assert.DoesNotContain("255000", json);
        Assert.DoesNotContain("deuda", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cuotas", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ElControladorExponeLaRutaVersionadaYLaAnteriorComoAlias()
    {
        var rutas = typeof(EstadoDeudaController)
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.RouteAttribute), false)
            .Cast<Microsoft.AspNetCore.Mvc.RouteAttribute>()
            .Select(r => r.Template)
            .ToArray();

        Assert.Contains("api/integracion/v1/estado-deuda", rutas);
        Assert.Contains("api/estado-deuda", rutas);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("123")]
    [InlineData("12345678901")]
    public void PorDni_UnDniInvalido_EsUnPedidoMalHecho(string dni)
    {
        var (controller, dao) = Crear();

        Assert.IsType<BadRequestObjectResult>(controller.PorDni(dni));
        Assert.False(dao.Llamado);
    }

    [Fact]
    public void PorDni_SiNoEsUnJugador_DaNoEncontrado()
    {
        var (controller, _) = Crear();

        Assert.IsType<NotFoundObjectResult>(controller.PorDni("41000001"));
    }

    [Fact]
    public void PorDni_ConDosJugadoresParaElMismoDni_NoAdivinaCualEs()
    {
        var (controller, dao) = Crear();
        dao.Jugadores.Add(new EstadoDeudaJugador(5, "41000001", 0, 0m, null));
        dao.Jugadores.Add(new EstadoDeudaJugador(6, "41000001", 3, 0m, null));

        Assert.IsType<ConflictObjectResult>(controller.PorDni("41000001"));
    }

    [Fact]
    public void PorDni_ConLaBaseCaida_Devuelve500SinFiltrarElError()
    {
        var (controller, dao) = Crear();
        dao.Falla = true;

        var resultado = Assert.IsType<ObjectResult>(controller.PorDni("41000001"));
        Assert.Equal(500, resultado.StatusCode);
        Assert.DoesNotContain("base caída", System.Text.Json.JsonSerializer.Serialize(resultado.Value));
    }

    [Theory]
    [InlineData("Don QR")]
    [InlineData("Médico")]
    [InlineData("Coordinador")]
    [InlineData("Administrador (Tesorero)")]
    [InlineData("1")]
    public void LosRolesQueVenATodos_NoSeLimitanPorCategoria(string rol)
    {
        var (controller, dao) = Crear(rol, 3);
        dao.Jugadores.Add(new EstadoDeudaJugador(5, "41000001", 0, 0m, null));

        controller.PorDni("41000001");

        Assert.Null(dao.UltimasCategorias);
    }

    [Theory]
    [InlineData("Director Técnico")]
    [InlineData("Preparador Físico")]
    public void ElStaffDeCategoria_VeSoloLasSuyas(string rol)
    {
        var (controller, dao) = Crear(rol, 3, 5, 3);

        controller.PorDni("41000001");

        Assert.Equal(new[] { 3, 5 }, dao.UltimasCategorias);
    }

    [Fact]
    public void ElStaffSinCategorias_NoVeANadie()
    {
        var (controller, dao) = Crear("Director Técnico");
        dao.Jugadores.Add(new EstadoDeudaJugador(5, "41000001", 0, 0m, null));

        Assert.IsType<NotFoundObjectResult>(controller.PorDni("41000001"));
        Assert.NotNull(dao.UltimasCategorias);
        Assert.Empty(dao.UltimasCategorias!);
    }

    [Fact]
    public void PorLote_SeparaLosEncontradosDeLosQueNo()
    {
        var (controller, dao) = Crear();
        dao.Jugadores.Add(new EstadoDeudaJugador(5, "41000001", 0, 0m, null));
        dao.Jugadores.Add(new EstadoDeudaJugador(6, "42000002", 2, 200_000m, null));

        var ok = Assert.IsType<OkObjectResult>(controller.PorLote(new EstadoDeudaLoteRequestDTO
        {
            Dnis = new List<string> { "41.000.001", "42000002", "42000002", "43000003", "xyz" }
        }));
        var respuesta = Assert.IsType<EstadoDeudaLoteResponseDTO>(ok.Value);

        Assert.Equal(new[] { "41000001", "42000002" }, respuesta.Items.Select(i => i.Dni));
        Assert.Equal("inhabilitado", respuesta.Items.Single(i => i.Dni == "42000002").Estado);
        Assert.Equal(new[] { "xyz", "43000003" }, respuesta.NoEncontrados);
        Assert.Equal(new[] { "41000001", "42000002", "43000003" }, dao.UltimosDni); // sin repetidos
    }

    [Fact]
    public void PorLote_SinDniOConDemasiados_EsUnPedidoMalHecho()
    {
        var (controller, dao) = Crear();

        Assert.IsType<BadRequestObjectResult>(controller.PorLote(new EstadoDeudaLoteRequestDTO { Dnis = null }));
        Assert.IsType<BadRequestObjectResult>(controller.PorLote(new EstadoDeudaLoteRequestDTO { Dnis = new List<string>() }));
        var demasiados = Enumerable.Range(0, EstadoDeudaController.MaximoPorLote + 1).Select(i => (40_000_000 + i).ToString()).ToList();
        Assert.IsType<BadRequestObjectResult>(controller.PorLote(new EstadoDeudaLoteRequestDTO { Dnis = demasiados }));
        Assert.False(dao.Llamado);
    }

    // El Administrador (rol 2) trabaja solo en el Portal Administrativo: no puede consultar el estado deportivo.
    [Fact]
    public void ElAdministradorDelAdministrativo_NoEstaEntreQuienesConsultan()
    {
        var roles = typeof(EstadoDeudaController).GetCustomAttribute<AuthorizeAttribute>()!.Roles!
            .Split(',', StringSplitOptions.TrimEntries);

        Assert.DoesNotContain("2", roles);
        Assert.DoesNotContain("Administrativo", roles);
        Assert.Contains("Don QR", roles);
        Assert.Contains("1", roles);
    }
}
