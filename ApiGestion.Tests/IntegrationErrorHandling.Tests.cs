namespace ApiGestion.Tests;

using System.Security.Claims;
using System.Text.Json;
using ApiGestion;
using ApiGestion.Controllers;
using ApiGestion.Models;
using ApiGestion.Services;
using DaoLibrary;
using EntityLibrary;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;
using ServiceLibrary;

// HU-058: errores de la integración con el Portal Deportivo. Contrato { errorCode, message, field? }, log con fecha y hora,
// endpoint y motivo sin datos sensibles, y fallas contenidas para que no alcancen al resto del Portal Administrativo.
internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        // Si alguien pasara la excepción al logger, su mensaje crudo terminaría en el archivo: se registra para detectarlo.
        Entries.Add((logLevel, formatter(state, exception) + (exception is null ? "" : " | " + exception)));
    }

    public string All => string.Join("\n", Entries.Select(e => e.Message));
}

public class LogSanitizerTests
{
    [Theory]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.abc-DEF_123", "eyJ")]
    [InlineData("token suelto eyJhbGciOiJIUzI1NiJ9.eyJpZFVzdWFyaW8iOiI3In0.firma", "eyJ")]
    [InlineData("Server=x;Database=y;User Id=cacc_api;Password=S3creta!;", "S3creta")]
    [InlineData("contrasenia='hunter2'", "hunter2")]
    [InlineData("api_key: 12ab34cd", "12ab34cd")]
    [InlineData("No se pudo enviar a juan.perez@gmail.com", "juan.perez@gmail.com")]
    [InlineData("El jugador con DNI 41000001 no existe", "41000001")]
    [InlineData("El jugador con DNI 41.000.001 no existe", "41.000.001")]
    public void SensitiveDataNeverReachesTheLog(string text, string secret)
    {
        string sanitized = LogSanitizer.Sanitize(text);

        Assert.DoesNotContain(secret, sanitized);
    }

    [Fact]
    public void OrdinaryTextIsKept()
    {
        Assert.Equal("El jugador 42 está dado de baja.", LogSanitizer.Sanitize("El jugador 42 está dado de baja."));
    }

    [Theory]
    [InlineData("/api/integracion/v1/estado-deuda/dni/41000001", "/api/integracion/v1/estado-deuda/dni/{valor}")]
    [InlineData("/api/estado-deuda/dni/41.000.001?x=1", "/api/estado-deuda/dni/{valor}")]
    [InlineData("/api/integracion/v1/jugadores/42/cargos-alta", "/api/integracion/v1/jugadores/{valor}/cargos-alta")]
    public void PathsLoseTheirValuesAndQueryString(string path, string expected)
    {
        Assert.Equal(expected, LogSanitizer.SanitizePath(path));
    }
}

public class IntegrationErrorHandlingMiddlewareTests
{
    private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJpZFVzdWFyaW8iOiI3In0.firma-secreta";

    private static DefaultHttpContext Request(string path, string? template = null)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        context.Request.Headers.Authorization = $"Bearer {Token}";
        context.Response.Body = new MemoryStream();
        if (template is not null)
        {
            context.SetEndpoint(new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(template), 0, EndpointMetadataCollection.Empty, null));
        }
        return context;
    }

    private static JsonElement Body(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(context.Response.Body).RootElement;
    }

    [Fact]
    public async Task AnUnhandledException_BecomesA500WithTheContract_WithoutInternalDetails()
    {
        var logger = new CapturingLogger<IntegrationErrorHandlingMiddleware>();
        var middleware = new IntegrationErrorHandlingMiddleware(
            _ => throw new InvalidOperationException("detalle interno del DNI 41000001"), logger);
        var context = Request("/api/integracion/v1/estado-deuda/dni/41000001", "api/integracion/v1/estado-deuda/dni/{dni}");

        await middleware.InvokeAsync(context);

        Assert.Equal(500, context.Response.StatusCode);
        var body = Body(context);
        Assert.Equal("INTERNAL_ERROR", body.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
        Assert.False(body.TryGetProperty("field", out _));
        Assert.DoesNotContain("detalle interno", body.GetRawText());
    }

    [Fact]
    public async Task TheLogHasTimestampEndpointAndReason_AndNoDniNorToken()
    {
        var logger = new CapturingLogger<IntegrationErrorHandlingMiddleware>();
        var middleware = new IntegrationErrorHandlingMiddleware(
            _ => throw new InvalidOperationException($"falló con DNI 41000001 y Bearer {Token}"), logger);
        var context = Request("/api/integracion/v1/estado-deuda/dni/41000001", "api/integracion/v1/estado-deuda/dni/{dni}");

        await middleware.InvokeAsync(context);

        var (level, message) = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, level);
        Assert.Contains("Timestamp: ", message);
        Assert.Contains("Endpoint: GET api/integracion/v1/estado-deuda/dni/{dni}", message);
        Assert.Contains("Motivo: InvalidOperationException", message);
        Assert.DoesNotContain("41000001", message);
        Assert.DoesNotContain(Token, message);
        Assert.DoesNotContain("eyJ", message);
    }

    [Fact]
    public async Task WithoutARouteTemplate_TheLoggedPathHidesItsValues()
    {
        var logger = new CapturingLogger<IntegrationErrorHandlingMiddleware>();
        var middleware = new IntegrationErrorHandlingMiddleware(_ => throw new InvalidOperationException("x"), logger);

        await middleware.InvokeAsync(Request("/api/estado-deuda/dni/41.000.001"));

        Assert.Contains("Endpoint: GET api/estado-deuda/dni/{valor}", logger.All);
        Assert.DoesNotContain("41.000.001", logger.All);
    }

    [Fact]
    public async Task ADatabaseOutage_Is503SoTheDeportivoCanRetry()
    {
        var middleware = new IntegrationErrorHandlingMiddleware(
            _ => throw new TimeoutException("Execution Timeout Expired"), new CapturingLogger<IntegrationErrorHandlingMiddleware>());
        var context = Request("/api/integracion/v1/estado-deuda/lote");

        await middleware.InvokeAsync(context);

        Assert.Equal(503, context.Response.StatusCode);
        Assert.Equal("SERVICE_UNAVAILABLE", Body(context).GetProperty("errorCode").GetString());
    }

    [Theory]
    [InlineData(401, "UNAUTHORIZED")]
    [InlineData(403, "FORBIDDEN")]
    [InlineData(429, "TOO_MANY_REQUESTS")]
    [InlineData(405, "METHOD_NOT_ALLOWED")]
    [InlineData(415, "UNSUPPORTED_MEDIA_TYPE")]
    public async Task EmptyFrameworkResponses_GetTheContractAndAreLogged(int status, string errorCode)
    {
        var logger = new CapturingLogger<IntegrationErrorHandlingMiddleware>();
        var middleware = new IntegrationErrorHandlingMiddleware(ctx => { ctx.Response.StatusCode = status; return Task.CompletedTask; }, logger);
        var context = Request("/api/integracion/v1/estado-deuda/dni/41000001", "api/integracion/v1/estado-deuda/dni/{dni}");

        await middleware.InvokeAsync(context);

        Assert.Equal(status, context.Response.StatusCode);
        var body = Body(context);
        Assert.Equal(errorCode, body.GetProperty("errorCode").GetString());
        Assert.Equal(body.GetProperty("message").GetString(), body.GetProperty("mensaje").GetString());
        Assert.False(body.GetProperty("exito").GetBoolean());
        Assert.Contains(errorCode, logger.All);
        Assert.DoesNotContain(Token, logger.All);
    }

    [Fact]
    public async Task A401KeepsTheWwwAuthenticateHeader()
    {
        var middleware = new IntegrationErrorHandlingMiddleware(ctx =>
        {
            ctx.Response.StatusCode = 401;
            ctx.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }, new CapturingLogger<IntegrationErrorHandlingMiddleware>());
        var context = Request("/api/integracion/v1/estado-deuda/lote");

        await middleware.InvokeAsync(context);

        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate.ToString());
    }

    [Fact]
    public async Task AnEmpty404StaysEmpty_ItMeansTheRouteDoesNotExist()
    {
        var middleware = new IntegrationErrorHandlingMiddleware(ctx => { ctx.Response.StatusCode = 404; return Task.CompletedTask; },
            new CapturingLogger<IntegrationErrorHandlingMiddleware>());
        var context = Request("/api/integracion/v1/no-existe");

        await middleware.InvokeAsync(context);

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Fact]
    public async Task AResponseTheEndpointAlreadyWrote_IsNotTouched()
    {
        var middleware = new IntegrationErrorHandlingMiddleware(async ctx =>
        {
            ctx.Response.StatusCode = 400;
            await ctx.Response.WriteAsJsonAsync(new { errorCode = "INVALID_INPUT", message = "x", field = "dni" });
        }, new CapturingLogger<IntegrationErrorHandlingMiddleware>());
        var context = Request("/api/integracion/v1/estado-deuda/dni/abc");

        await middleware.InvokeAsync(context);

        Assert.Equal("dni", Body(context).GetProperty("field").GetString());
    }

    [Theory]
    [InlineData("/api/integracion/v1/estado-deuda/dni/1", true)]
    [InlineData("/api/integracion/v1/jugadores/1/cargos-alta", true)]
    [InlineData("/api/estado-deuda/lote", true)]
    [InlineData("/api/pagos/cobro", false)]
    [InlineData("/api/aranceles/programar", false)]
    public void OnlyTheIntegrationRoutesUseThisHandling(string path, bool isIntegration)
    {
        Assert.Equal(isIntegration, IntegrationErrors.IsIntegrationRequest(path));
    }
}

public class IntegrationControllersErrorContractTests
{
    private sealed class FailingEstadoDeudaDao(Exception? error = null) : IEstadoDeudaDao
    {
        public IReadOnlyList<EstadoDeudaJugador> ObtenerPorDni(IReadOnlyCollection<string> dnis, IReadOnlyCollection<int>? categoriasPermitidas) =>
            error is null ? new List<EstadoDeudaJugador>() : throw error;
    }

    private sealed class FakeAltaService(Func<ResultadoOperacion<CargosGenerados>> result) : IJugadoresAltaService
    {
        public ResultadoOperacion<CargosGenerados> GenerarCargosDeAlta(int idJugador) => result();
        public ResultadoOperacion<AltaJugadorRegistrada> Registrar(SolicitudAltaJugador solicitud) => throw new NotSupportedException();
    }

    private static T WithUser<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Don QR") }, "Bearer"))
            }
        };
        return controller;
    }

    private static IntegrationErrorResponseDTO Error(IActionResult result) =>
        Assert.IsType<IntegrationErrorResponseDTO>(Assert.IsAssignableFrom<ObjectResult>(result).Value);

    [Fact]
    public void AnInvalidDni_Is400WithTheField()
    {
        var logger = new CapturingLogger<EstadoDeudaController>();
        var controller = WithUser(new EstadoDeudaController(new FailingEstadoDeudaDao(), logger));

        var result = controller.PorDni("abc");

        Assert.IsType<BadRequestObjectResult>(result);
        var error = Error(result);
        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal("dni", error.Field);
        Assert.Contains("DNI válido", error.Message);
        Assert.Contains("INVALID_INPUT", logger.All);
    }

    [Fact]
    public void AnEmptyBatch_Is400OnDnis()
    {
        var controller = WithUser(new EstadoDeudaController(new FailingEstadoDeudaDao(), new CapturingLogger<EstadoDeudaController>()));

        var error = Error(controller.PorLote(new EstadoDeudaLoteRequestDTO()));

        Assert.Equal("dnis", error.Field);
    }

    [Fact]
    public void AnUnknownDni_Is404WithItsCode()
    {
        var controller = WithUser(new EstadoDeudaController(new FailingEstadoDeudaDao(), new CapturingLogger<EstadoDeudaController>()));

        var result = controller.PorDni("41000001");

        Assert.IsType<NotFoundObjectResult>(result);
        Assert.Equal("NOT_FOUND", Error(result).ErrorCode);
        Assert.Null(Error(result).Field);
    }

    [Fact]
    public void ADatabaseFailure_DoesNotEscape_AndTheLogHasNoDni()
    {
        var logger = new CapturingLogger<EstadoDeudaController>();
        var controller = WithUser(new EstadoDeudaController(
            new FailingEstadoDeudaDao(new TimeoutException("timeout buscando 41000001")), logger));

        var result = Assert.IsType<ObjectResult>(controller.PorDni("41000001"));

        Assert.Equal(503, result.StatusCode);
        Assert.Equal("SERVICE_UNAVAILABLE", Error(result).ErrorCode);
        Assert.DoesNotContain("41000001", logger.All);
    }

    [Fact]
    public void CargosDeAlta_InvalidId_Is400OnIdJugador()
    {
        var controller = WithUser(new IntegracionJugadoresController(
            new FakeAltaService(() => throw new InvalidOperationException()), new CapturingLogger<IntegracionJugadoresController>()));

        var error = Error(controller.CargosDeAlta(0));

        Assert.Equal("idJugador", error.Field);
    }

    [Fact]
    public void CargosDeAlta_AFailureInTheService_IsAControlledResponse()
    {
        var controller = WithUser(new IntegracionJugadoresController(
            new FakeAltaService(() => throw new InvalidOperationException("se cayó")), new CapturingLogger<IntegracionJugadoresController>()));

        var result = Assert.IsType<ObjectResult>(controller.CargosDeAlta(7));

        Assert.Equal(500, result.StatusCode);
        Assert.Equal("INTERNAL_ERROR", Error(result).ErrorCode);
        Assert.DoesNotContain("se cayó", JsonSerializer.Serialize(result.Value));
    }

    [Theory]
    [InlineData(TipoResultado.NoEncontrado, 404, "NOT_FOUND")]
    [InlineData(TipoResultado.NoProcesable, 422, "UNPROCESSABLE")]
    [InlineData(TipoResultado.Conflicto, 409, "CONFLICT")]
    public void CargosDeAlta_BusinessFailures_UseTheContract(TipoResultado tipo, int status, string errorCode)
    {
        var controller = WithUser(new IntegracionJugadoresController(
            new FakeAltaService(() => ResultadoOperacion<CargosGenerados>.Falla(tipo, "No corresponde.")),
            new CapturingLogger<IntegracionJugadoresController>()));

        var result = Assert.IsAssignableFrom<ObjectResult>(controller.CargosDeAlta(7));

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(errorCode, Error(result).ErrorCode);
        Assert.Equal("No corresponde.", Error(result).Message);
    }

    // Compatibilidad v1: el Portal Deportivo ya lee exito y mensaje; siguen viniendo junto al contrato nuevo.
    [Fact]
    public void TheSerializedErrorKeepsTheV1Fields()
    {
        var json = JsonDocument.Parse(JsonSerializer.Serialize(IntegrationErrors.Body("INVALID_INPUT", "Indicá un DNI válido.", "dni"))).RootElement;

        Assert.Equal("INVALID_INPUT", json.GetProperty("errorCode").GetString());
        Assert.Equal("Indicá un DNI válido.", json.GetProperty("message").GetString());
        Assert.Equal("dni", json.GetProperty("field").GetString());
        Assert.False(json.GetProperty("exito").GetBoolean());
        Assert.Equal("Indicá un DNI válido.", json.GetProperty("mensaje").GetString());
        Assert.DoesNotContain("\"field\"", JsonSerializer.Serialize(IntegrationErrors.Body("NOT_FOUND", "x")));
    }

    [Fact]
    public void AnUnreadableBody_Is400WithTheFieldAndNoParserDetails()
    {
        var logger = new CapturingLogger<EstadoDeudaController>();
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        context.ModelState.AddModelError("$.dnis[0]", "The JSON value could not be converted to System.String. Path: $.dnis[0] | 41000001");

        var result = Assert.IsType<BadRequestObjectResult>(IntegrationErrors.FromInvalidModelState(context, logger));
        var error = Assert.IsType<IntegrationErrorResponseDTO>(result.Value);

        Assert.Equal("INVALID_INPUT", error.ErrorCode);
        Assert.Equal("dnis[0]", error.Field);
        Assert.DoesNotContain("System.String", error.Message);
        Assert.DoesNotContain("41000001", logger.All);
    }

    [Fact]
    public void AMissingBody_Is400WithoutField()
    {
        var context = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());
        context.ModelState.AddModelError("request", "The request field is required.");

        var error = Assert.IsType<IntegrationErrorResponseDTO>(
            Assert.IsType<BadRequestObjectResult>(IntegrationErrors.FromInvalidModelState(context, new CapturingLogger<EstadoDeudaController>())).Value);

        Assert.Null(error.Field);
    }

    private sealed class FakeProblemDetailsFactory : Microsoft.AspNetCore.Mvc.Infrastructure.ProblemDetailsFactory
    {
        public override ProblemDetails CreateProblemDetails(HttpContext httpContext, int? statusCode = null, string? title = null,
            string? type = null, string? detail = null, string? instance = null) => new() { Status = statusCode, Title = "problem" };

        public override ValidationProblemDetails CreateValidationProblemDetails(HttpContext httpContext, ModelStateDictionary modelStateDictionary,
            int? statusCode = null, string? title = null, string? type = null, string? detail = null, string? instance = null) => new();
    }

    private sealed class Status415 : Microsoft.AspNetCore.Mvc.Infrastructure.IClientErrorActionResult
    {
        public int? StatusCode => 415;
        public Task ExecuteResultAsync(ActionContext context) => Task.CompletedTask;
    }

    [Theory]
    [InlineData("/api/integracion/v1/estado-deuda/lote", true)]
    [InlineData("/api/aranceles/programar", false)]
    public void MvcClientErrors_UseTheContractOnlyInIntegration(string path, bool isIntegration)
    {
        var factory = new IntegrationClientErrorFactory(new FakeProblemDetailsFactory(), Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Path = path;

        var result = Assert.IsType<ObjectResult>(factory.GetClientError(new ActionContext(httpContext, new RouteData(), new ActionDescriptor()), new Status415()));

        Assert.Equal(415, result.StatusCode);
        if (isIntegration)
        {
            Assert.Equal("UNSUPPORTED_MEDIA_TYPE", Assert.IsType<IntegrationErrorResponseDTO>(result.Value).ErrorCode);
        }
        else
        {
            Assert.IsType<ProblemDetails>(result.Value);
        }
    }

    // Aislamiento: los dos controllers de integración comparten el cupo de consultas simultáneas, separado del resto del Portal.
    [Theory]
    [InlineData(typeof(EstadoDeudaController))]
    [InlineData(typeof(IntegracionJugadoresController))]
    public void IntegrationControllersUseTheIsolatedRateLimitPolicy(Type controller)
    {
        var attribute = Assert.Single(controller.GetCustomAttributes(typeof(EnableRateLimitingAttribute), false).Cast<EnableRateLimitingAttribute>());

        Assert.Equal(IntegrationErrors.RateLimitPolicy, attribute.PolicyName);
    }
}
