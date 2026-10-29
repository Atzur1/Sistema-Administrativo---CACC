namespace ApiGestion.Tests;

using System.Text.Json;
using ApiGestion;
using ApiGestion.Services;
using DaoLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

// HU-055: credencial de sistema de la API de integración. El backend del Portal Deportivo manda X-Integration-Key además
// del token del usuario. Sin la credencial (o con una que no es) la integración responde 401 sin datos y sin llegar al
// endpoint; con la credencial válida el pedido sigue a la autenticación y los permisos de siempre, y las escrituras quedan
// auditadas con origen "PortalDeportivo". La credencial no es un usuario: no da identidad, ni roles, ni sesión.
public class IntegrationClientAuthenticationMiddlewareTests
{
    private const string Clave = "clave-actual-de-prueba-0123456789abcdefABCDEF";
    private const string ClaveAnterior = "clave-anterior-de-prueba-0123456789abcdefABC";

    private static IntegrationClientCredentials Credenciales(string? anterior = null) =>
        IntegrationClientCredentials.FromConfiguration(Configuracion(Clave, anterior));

    private static IConfiguration Configuracion(string? clave, string? anterior = null)
    {
        var valores = new Dictionary<string, string?> { [IntegrationClientCredentials.ConfigClave] = clave };
        if (anterior is not null) valores[IntegrationClientCredentials.ConfigClaveAnterior] = anterior;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static DefaultHttpContext Pedido(params string[] claves)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/integracion/v1/estado-deuda/dni/41000001";
        // Con el formato real de Kestrel: el sufijo de 8 dígitos se parece a un DNI y no tiene que quedar oculto.
        context.TraceIdentifier = "0HNPLA02E48M1:00000001";
        if (claves.Length > 0) context.Request.Headers[IntegrationClientCredentials.Header] = claves;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<(bool LlegoAlEndpoint, CapturingLogger<IntegrationClientAuthenticationMiddleware> Log)> Ejecutar(
        DefaultHttpContext context, IntegrationClientCredentials credenciales, Action<HttpContext>? enElEndpoint = null)
    {
        bool llego = false;
        var log = new CapturingLogger<IntegrationClientAuthenticationMiddleware>();
        var middleware = new IntegrationClientAuthenticationMiddleware(c =>
        {
            llego = true;
            enElEndpoint?.Invoke(c);
            return Task.CompletedTask;
        }, credenciales, log);
        await middleware.InvokeAsync(context);
        return (llego, log);
    }

    private static JsonElement Cuerpo(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement;
    }

    private static string Texto(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return new StreamReader(context.Response.Body).ReadToEnd();
    }

    [Fact]
    public async Task WithoutTheCredential_Responds401WithTheContractBody_AndNeverReachesTheEndpoint()
    {
        var context = Pedido();

        var (llego, _) = await Ejecutar(context, Credenciales());

        Assert.False(llego);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal(IntegrationErrorCodes.Unauthorized, Cuerpo(context).GetProperty("errorCode").GetString());
        Assert.False(Cuerpo(context).GetProperty("exito").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("clave-incorrecta")]
    [InlineData(Clave + "x")]
    [InlineData("CLAVE-ACTUAL-DE-PRUEBA-0123456789ABCDEFABCDEF")]
    public async Task WithAnEmptyOrWrongCredential_Responds401_AndTheAnswerGivesNothingAway(string enviada)
    {
        var context = Pedido(enviada);

        var (llego, _) = await Ejecutar(context, Credenciales());

        Assert.False(llego);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        string respuesta = Texto(context);
        Assert.DoesNotContain(Clave, respuesta);
        Assert.True(enviada.Trim().Length == 0 || !respuesta.Contains(enviada), "La respuesta no repite la credencial enviada.");
    }

    [Fact]
    public async Task WithTheCredentialSentTwice_Responds401()
    {
        var context = Pedido(Clave, Clave);

        var (llego, _) = await Ejecutar(context, Credenciales());

        Assert.False(llego);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithTheValidCredential_ContinuesToTheEndpoint_StampingTheDeportivoAsAuditOrigin()
    {
        string? origenEnElEndpoint = null;
        var context = Pedido(Clave);

        var (llego, _) = await Ejecutar(context, Credenciales(), _ => origenEnElEndpoint = SqlAuditActorContext.CurrentSystem);

        Assert.True(llego);
        Assert.Equal(IntegrationClientCredentials.SistemaDeportivo, origenEnElEndpoint);
        Assert.Null(SqlAuditActorContext.CurrentSystem);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task TheCredentialIsNotAUser_ItGivesNoIdentityRolesOrSession()
    {
        bool autenticado = true;
        int claims = -1;
        var context = Pedido(Clave);

        await Ejecutar(context, Credenciales(), c =>
        {
            autenticado = c.User.Identity?.IsAuthenticated ?? false;
            claims = c.User.Claims.Count();
        });

        Assert.False(autenticado);
        Assert.Equal(0, claims);
        Assert.False(context.Response.Headers.ContainsKey("Set-Cookie"));
    }

    [Fact]
    public async Task DuringARotation_TheCurrentAndThePreviousCredentialAreAccepted()
    {
        var credenciales = Credenciales(ClaveAnterior);

        Assert.True((await Ejecutar(Pedido(Clave), credenciales)).LlegoAlEndpoint);
        Assert.True((await Ejecutar(Pedido(ClaveAnterior), credenciales)).LlegoAlEndpoint);
    }

    [Fact]
    public async Task AfterTheRotation_TheOldCredentialIsRefused()
    {
        var context = Pedido(ClaveAnterior);

        var (llego, _) = await Ejecutar(context, Credenciales());

        Assert.False(llego);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task TheLogHasEndpointResultAndCorrelation_ButNeverTheCredential()
    {
        var rechazado = Pedido("clave-equivocada-que-no-tiene-que-quedar-en-el-log");
        var (_, logRechazo) = await Ejecutar(rechazado, Credenciales());
        var aceptado = Pedido(Clave);
        var (_, logAceptado) = await Ejecutar(aceptado, Credenciales());

        Assert.Contains("Correlación: 0HNPLA02E48M1:00000001", logRechazo.All);
        Assert.Contains("api/integracion/v1/estado-deuda/dni/{valor}", logRechazo.All);
        Assert.Contains("UNAUTHORIZED", logRechazo.All);
        Assert.DoesNotContain("clave-equivocada", logRechazo.All);
        Assert.DoesNotContain("41000001", logRechazo.All);
        Assert.DoesNotContain(Clave, logRechazo.All + logAceptado.All);
    }
}

public class IntegrationClientCredentialsConfigurationTests
{
    private static IConfiguration Configuracion(string? clave, string? anterior = null)
    {
        var valores = new Dictionary<string, string?>();
        if (clave is not null) valores[IntegrationClientCredentials.ConfigClave] = clave;
        if (anterior is not null) valores[IntegrationClientCredentials.ConfigClaveAnterior] = anterior;
        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("1234")]
    [InlineData("corta-de-menos-de-32-caracteres")]
    [InlineData("REEMPLAZAR-con-una-clave-generada-al-azar-de-32-bytes")]
    public void WithoutAStrongCredential_TheApiDoesNotStart_AndTheErrorNamesTheSettingWithoutAValue(string? clave)
    {
        var error = Assert.Throws<InvalidOperationException>(() => IntegrationClientCredentials.FromConfiguration(Configuracion(clave)));

        Assert.Contains(IntegrationClientCredentials.ConfigClave, error.Message);
        if (!string.IsNullOrWhiteSpace(clave)) Assert.DoesNotContain(clave, error.Message);
    }

    // Kestrel recorta los espacios de los extremos del header: una clave configurada con un salto de línea al final (típico
    // de un archivo de secreto o de un .env) nunca coincidiría y todas las llamadas darían 401 sin ninguna pista.
    [Theory]
    [InlineData("clave-actual-de-prueba-0123456789abcdefABCDEF\n")]
    [InlineData("clave-actual-de-prueba-0123456789abcdefABCDEF ")]
    [InlineData(" clave-actual-de-prueba-0123456789abcdefABCDEF")]
    public void ACredentialWithSpacesAtTheEnds_StopsTheStart_BecauseTheHeaderWouldNeverMatch(string clave)
    {
        var error = Assert.Throws<InvalidOperationException>(() => IntegrationClientCredentials.FromConfiguration(Configuracion(clave)));

        Assert.Contains(IntegrationClientCredentials.ConfigClave, error.Message);
        Assert.Contains("espacios", error.Message);
        Assert.DoesNotContain(clave.Trim(), error.Message);
    }

    [Fact]
    public void APreviousCredentialWithSpacesAtTheEnds_AlsoStopsTheStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() => IntegrationClientCredentials.FromConfiguration(
            Configuracion("clave-actual-de-prueba-0123456789abcdefABCDEF", "clave-anterior-de-prueba-0123456789abcdefABC\r\n")));

        Assert.Contains(IntegrationClientCredentials.ConfigClaveAnterior, error.Message);
    }

    [Fact]
    public void APreviousCredentialThatIsWeak_AlsoStopsTheStart()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            IntegrationClientCredentials.FromConfiguration(Configuracion("clave-actual-de-prueba-0123456789abcdefABCDEF", "1234")));

        Assert.Contains(IntegrationClientCredentials.ConfigClaveAnterior, error.Message);
        Assert.DoesNotContain("1234", error.Message);
    }

    // Terminar una rotación dejando la variable de la clave anterior vacía (en lugar de borrarla) no tiene que impedir el
    // arranque: vacía significa "no hay clave anterior", y una clave vacía nunca es válida.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyPreviousCredential_MeansThereIsNone(string anterior)
    {
        var credenciales = IntegrationClientCredentials.FromConfiguration(Configuracion("clave-actual-de-prueba-0123456789abcdefABCDEF", anterior));

        Assert.True(credenciales.IsValid("clave-actual-de-prueba-0123456789abcdefABCDEF"));
        Assert.False(credenciales.IsValid(anterior));
    }

    [Fact]
    public void AStrongCredential_IsAccepted()
    {
        var credenciales = IntegrationClientCredentials.FromConfiguration(Configuracion("clave-actual-de-prueba-0123456789abcdefABCDEF"));

        Assert.True(credenciales.IsValid("clave-actual-de-prueba-0123456789abcdefABCDEF"));
        Assert.False(credenciales.IsValid(null));
    }
}
