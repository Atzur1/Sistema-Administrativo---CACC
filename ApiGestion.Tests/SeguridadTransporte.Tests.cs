namespace ApiGestion.Tests;

using System.Net;
using System.Text.Json;
using ApiGestion;
using ApiGestion.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

// Code review de HU-055: el JWT y X-Integration-Key solo se aceptan por HTTPS, y X-Forwarded-For / X-Forwarded-Proto solo de
// un proxy confiable. Se prueba la misma cadena que arma Program.cs: ForwardedHeaders -> HttpsObligatorio -> resto.
public class SeguridadTransporteTests
{
    private const string Integracion = "/api/integracion/v1/estado-deuda/dni/41000001";

    private static IConfiguration Config(params (string Clave, string Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Clave, v => (string?)v.Valor)).Build();

    private static DefaultHttpContext Pedido(string desde, string ruta = Integracion, string? forwardedFor = null, string? forwardedProto = null,
        string esquema = "http")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Scheme = esquema;
        context.Request.Path = ruta;
        context.Connection.RemoteIpAddress = IPAddress.Parse(desde);
        context.Request.Headers["Authorization"] = "Bearer token-de-prueba";
        context.Request.Headers[IntegrationClientCredentials.Header] = "credencial-de-prueba";
        if (forwardedFor is not null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
        if (forwardedProto is not null) context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;
        context.Response.Body = new MemoryStream();
        return context;
    }

    // La cadena de Program.cs. Devuelve si el pedido llegó al resto del pipeline (credencial, autenticación, endpoint).
    private static async Task<bool> Pipeline(HttpContext context, IConfiguration config)
    {
        var opciones = new ForwardedHeadersOptions();
        ProxyConfiable.Configurar(opciones, config);
        bool llego = false;
        var https = new HttpsObligatorioMiddleware(_ => { llego = true; return Task.CompletedTask; }, NullLogger<HttpsObligatorioMiddleware>.Instance);
        var proxy = new ForwardedHeadersMiddleware(https.InvokeAsync, NullLoggerFactory.Instance, Options.Create(opciones));
        await proxy.Invoke(context);
        return llego;
    }

    private static JsonElement Cuerpo(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement;
    }

    // ===== HTTPS obligatorio =====

    [Fact]
    public async Task UnPedidoHttpConCredencialYToken_SeRechazaCon403_YNoLlegaAValidarse()
    {
        var context = Pedido("203.0.113.7");

        bool llego = await Pipeline(context, Config());

        Assert.False(llego);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("FORBIDDEN", Cuerpo(context).GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task UnPedidoHttpFueraDeLaIntegracion_TambienSeRechaza_ConElFormatoDelPortal()
    {
        var context = Pedido("203.0.113.7", "/api/auth/login");

        bool llego = await Pipeline(context, Config());

        Assert.False(llego);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.False(Cuerpo(context).GetProperty("exito").GetBoolean());
    }

    [Fact]
    public async Task UnPedidoHttpsDirecto_Sigue()
    {
        var context = Pedido("203.0.113.7", esquema: "https");

        Assert.True(await Pipeline(context, Config()));
        Assert.Equal(200, context.Response.StatusCode);
    }

    [Fact]
    public async Task LaRespuestaNoRedirige_NiDevuelveLaCredencial()
    {
        var context = Pedido("203.0.113.7");
        await Pipeline(context, Config());

        Assert.False(context.Response.Headers.ContainsKey("Location"));
        context.Response.Body.Position = 0;
        string texto = new StreamReader(context.Response.Body).ReadToEnd();
        Assert.DoesNotContain("credencial-de-prueba", texto);
        Assert.DoesNotContain("token-de-prueba", texto);
    }

    // ===== Proxies confiables =====

    [Fact]
    public async Task UnClienteDeInternet_NoPuedeDeclararseHttps_NiFalsearSuIp()
    {
        var context = Pedido("203.0.113.7", forwardedFor: "198.51.100.1", forwardedProto: "https");

        bool llego = await Pipeline(context, Config());

        Assert.False(llego);
        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
    }

    [Theory]
    [InlineData("172.18.0.5")]           // red interna de docker compose (nginx)
    [InlineData("::ffff:172.18.0.5")]    // la misma, informada como IPv4 mapeada en IPv6
    [InlineData("10.1.2.3")]
    [InlineData("127.0.0.1")]
    public async Task ElProxyDeUnaRedPrivada_InformaIpYEsquema_PorDefecto(string proxy)
    {
        var context = Pedido(proxy, forwardedFor: "198.51.100.1", forwardedProto: "https");

        bool llego = await Pipeline(context, Config());

        Assert.True(llego);
        Assert.Equal(IPAddress.Parse("198.51.100.1"), context.Connection.RemoteIpAddress);
        Assert.Equal("https", context.Request.Scheme);
    }

    [Fact]
    public async Task ElProxyDeConfianzaPorDefecto_ConProtoHttp_SeRechaza()
    {
        // nginx con PUBLIC_SCHEME=http (sin TLS delante): el pedido original fue HTTP y la API no lo acepta.
        var context = Pedido("172.18.0.5", forwardedFor: "198.51.100.1", forwardedProto: "http");

        Assert.False(await Pipeline(context, Config()));
        Assert.Equal(403, context.Response.StatusCode);
    }

    [Fact]
    public async Task ConUnaListaExplicita_SoloEsaIpEsProxy()
    {
        var config = Config((ProxyConfiable.ConfigProxies, "10.0.0.9"));

        var desdeOtraRedPrivada = Pedido("172.18.0.5", forwardedFor: "198.51.100.1", forwardedProto: "https");
        var desdeElProxy = Pedido("10.0.0.9", forwardedFor: "198.51.100.1", forwardedProto: "https");

        Assert.False(await Pipeline(desdeOtraRedPrivada, config));
        Assert.Equal(IPAddress.Parse("172.18.0.5"), desdeOtraRedPrivada.Connection.RemoteIpAddress);
        Assert.True(await Pipeline(desdeElProxy, config));
        Assert.Equal(IPAddress.Parse("198.51.100.1"), desdeElProxy.Connection.RemoteIpAddress);
    }

    [Fact]
    public async Task LasRedesSeAceptanComoListaSeparadaPorComas()
    {
        var config = Config((ProxyConfiable.ConfigRedes, "100.64.0.0/10, 192.0.2.0/24"));

        Assert.True(await Pipeline(Pedido("100.64.3.4", forwardedFor: "198.51.100.1", forwardedProto: "https"), config));
        Assert.False(await Pipeline(Pedido("10.1.2.3", forwardedFor: "198.51.100.1", forwardedProto: "https"), config));
    }

    [Theory]
    [InlineData(ProxyConfiable.ConfigRedes, "10.0.0.0")]
    [InlineData(ProxyConfiable.ConfigRedes, "10.0.0.0/33")]
    [InlineData(ProxyConfiable.ConfigRedes, "no-es-una-red/8")]
    [InlineData(ProxyConfiable.ConfigProxies, "proxy.local")]
    public void UnaIpORedMalEscrita_NoDejaArrancar(string clave, string valor)
    {
        Assert.Throws<InvalidOperationException>(() => ProxyConfiable.Configurar(new ForwardedHeadersOptions(), Config((clave, valor))));
    }

    [Fact]
    public void SinListaExplicita_SeAvisaQueSeUsanLasRedesPorDefecto()
    {
        Assert.True(ProxyConfiable.Configurar(new ForwardedHeadersOptions(), Config()));
        Assert.False(ProxyConfiable.Configurar(new ForwardedHeadersOptions(), Config((ProxyConfiable.ConfigProxies, "10.0.0.9"))));
    }

    // ===== Contrato OpenAPI del alias /api/estado-deuda =====

    private static OpenApiDocument Documento()
    {
        var documento = new OpenApiDocument
        {
            Paths = new OpenApiPaths
            {
                ["/api/estado-deuda/dni/{dni}"] = new() { Operations = { [OperationType.Get] = new OpenApiOperation() } },
                ["/api/estado-deuda/lote"] = new() { Operations = { [OperationType.Post] = new OpenApiOperation() } },
                ["/api/jugadores"] = new() { Operations = { [OperationType.Get] = new OpenApiOperation() } }
            },
            Components = new OpenApiComponents()
        };
        documento.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme();
        documento.Components.SecuritySchemes["IntegrationKey"] = new OpenApiSecurityScheme();
        return documento;
    }

    // En Swashbuckle 6.6.2, DocumentFilterContext.DocumentName es el del SchemaRepository.
    private static DocumentFilterContext Contexto(string documento)
    {
        var contexto = new DocumentFilterContext(Array.Empty<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription>(), null!, new SchemaRepository(documento));
        Assert.Equal(documento, contexto.DocumentName);
        return contexto;
    }

    private static string[] Esquemas(OpenApiOperation operacion) =>
        operacion.Security.SelectMany(r => r.Keys.Select(k => k.Reference.Id)).OrderBy(x => x).ToArray();

    [Fact]
    public void ElAliasEstadoDeuda_DocumentaCredencialYToken_ComoExigeLaImplementacion()
    {
        var documento = Documento();

        new IntegrationKeySecurityFilter().Apply(documento, Contexto("v1"));

        Assert.Equal(new[] { "Bearer", "IntegrationKey" }, Esquemas(documento.Paths["/api/estado-deuda/dni/{dni}"].Operations[OperationType.Get]));
        Assert.Equal(new[] { "Bearer", "IntegrationKey" }, Esquemas(documento.Paths["/api/estado-deuda/lote"].Operations[OperationType.Post]));
        Assert.Empty(documento.Paths["/api/jugadores"].Operations[OperationType.Get].Security);
        Assert.True(documento.Components.SecuritySchemes.ContainsKey("IntegrationKey"));
    }

    [Fact]
    public void SinRutasDeIntegracion_ElPortalNoPublicaLaCredencial()
    {
        var documento = Documento();
        documento.Paths.Remove("/api/estado-deuda/dni/{dni}");
        documento.Paths.Remove("/api/estado-deuda/lote");

        new IntegrationKeySecurityFilter().Apply(documento, Contexto("v1"));

        Assert.False(documento.Components.SecuritySchemes.ContainsKey("IntegrationKey"));
    }

    [Fact]
    public void ElDocumentoDeIntegracion_PideLasDosCosasEnTodasLasOperaciones()
    {
        var documento = Documento();

        new IntegrationKeySecurityFilter().Apply(documento, Contexto("integracion-v1"));

        Assert.Equal(new[] { "Bearer", "IntegrationKey" }, documento.SecurityRequirements.Single().Keys.Select(k => k.Reference.Id).OrderBy(x => x).ToArray());
    }
}
