using System.Data.Common;
using ApiGestion.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using ServiceLibrary;

namespace ApiGestion.Services;

// Códigos de error del contrato de integración. Son estables: el Portal Deportivo puede decidir por ellos.
public static class IntegrationErrorCodes
{
    public const string InvalidInput = "INVALID_INPUT";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string Forbidden = "FORBIDDEN";
    public const string NotFound = "NOT_FOUND";
    public const string MethodNotAllowed = "METHOD_NOT_ALLOWED";
    public const string Conflict = "CONFLICT";
    public const string UnsupportedMediaType = "UNSUPPORTED_MEDIA_TYPE";
    public const string Unprocessable = "UNPROCESSABLE";
    public const string TooManyRequests = "TOO_MANY_REQUESTS";
    public const string InternalError = "INTERNAL_ERROR";
    public const string ServiceUnavailable = "SERVICE_UNAVAILABLE";
}

// Manejo centralizado de los errores de la API de integración con el Portal Deportivo (HU-058): arma la respuesta
// { errorCode, message, field? } y deja en el log fecha y hora, endpoint y motivo, siempre sanitizados.
// Lo usan los controllers de integración, el middleware y la respuesta automática a los modelos inválidos.
public static class IntegrationErrors
{
    // Política de rate limiting que separa la integración del resto del Portal (ver Program.cs).
    public const string RateLimitPolicy = "integracion";

    public static bool IsIntegrationRequest(PathString path) =>
        path.StartsWithSegments("/api/integracion", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/estado-deuda", StringComparison.OrdinalIgnoreCase);

    public static IntegrationErrorResponseDTO Body(string errorCode, string message, string? field = null) =>
        new() { ErrorCode = errorCode, Message = message, Field = field };

    // Error de negocio o de validación producido por un controller de integración.
    public static IActionResult Respond(ControllerBase controller, ILogger logger, int status, string errorCode, string message, string? field = null)
    {
        Log(logger, controller.HttpContext, status, errorCode, field is null ? message : $"{message} (campo: {field})");
        var body = Body(errorCode, message, field);
        return status switch
        {
            StatusCodes.Status400BadRequest => controller.BadRequest(body),
            StatusCodes.Status404NotFound => controller.NotFound(body),
            StatusCodes.Status409Conflict => controller.Conflict(body),
            StatusCodes.Status422UnprocessableEntity => controller.UnprocessableEntity(body),
            _ => new ObjectResult(body) { StatusCode = status }
        };
    }

    // Una falla interna (la base, un error de programación) termina en una respuesta controlada: nunca sube como excepción.
    public static IActionResult FromException(ControllerBase controller, ILogger logger, Exception exception)
    {
        var (status, errorCode, message) = Classify(exception);
        LogException(logger, controller.HttpContext, status, errorCode, exception);
        return new ObjectResult(Body(errorCode, message)) { StatusCode = status };
    }

    // Como ResultadoHttp.Responder, pero con el contrato de integración.
    public static IActionResult FromResult<T>(ControllerBase controller, ILogger logger, ResultadoOperacion<T> result, Func<T, IActionResult> onSuccess)
    {
        if (result.Exito)
        {
            return onSuccess(result.Valor!);
        }
        var (status, errorCode) = result.Tipo switch
        {
            TipoResultado.NoEncontrado => (StatusCodes.Status404NotFound, IntegrationErrorCodes.NotFound),
            TipoResultado.Conflicto => (StatusCodes.Status409Conflict, IntegrationErrorCodes.Conflict),
            TipoResultado.NoProcesable => (StatusCodes.Status422UnprocessableEntity, IntegrationErrorCodes.Unprocessable),
            _ => (StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput)
        };
        return Respond(controller, logger, status, errorCode, result.Mensaje ?? "No se pudo procesar el pedido.");
    }

    // Respuesta a un cuerpo que no se puede leer (JSON mal formado, tipo equivocado). El detalle de System.Text.Json
    // no se devuelve ni se registra: puede traer parte de lo que se envió.
    public static IActionResult FromInvalidModelState(ActionContext context, ILogger logger)
    {
        string? field = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => FieldName(entry.Key))
            .FirstOrDefault(name => name is not null);
        string message = field is null
            ? "El cuerpo del pedido falta o no es un JSON válido."
            : $"El dato \"{field}\" no tiene un formato válido.";
        Log(logger, context.HttpContext, StatusCodes.Status400BadRequest, IntegrationErrorCodes.InvalidInput,
            field is null ? "Cuerpo del pedido ausente o ilegible." : $"Dato de entrada con formato inválido (campo: {field}).");
        return new BadRequestObjectResult(Body(IntegrationErrorCodes.InvalidInput, message, field));
    }

    // "$.dnis[3]" -> "dnis[3]"; "request" o "" (el cuerpo entero) -> null.
    internal static string? FieldName(string modelStateKey)
    {
        string key = modelStateKey.StartsWith("$", StringComparison.Ordinal) ? modelStateKey.TrimStart('$').TrimStart('.') : modelStateKey;
        if (string.IsNullOrWhiteSpace(key) || key.Equals("request", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return key.Length > 1 ? char.ToLowerInvariant(key[0]) + key[1..] : key.ToLowerInvariant();
    }

    // Una caída de la base (o un timeout) es "servicio no disponible": el Deportivo puede reintentar. Lo demás es un 500.
    public static (int Status, string ErrorCode, string Message) Classify(Exception exception) => exception switch
    {
        DbException or TimeoutException => (StatusCodes.Status503ServiceUnavailable, IntegrationErrorCodes.ServiceUnavailable,
            "El Portal Administrativo no puede consultar los datos en este momento. Reintentá en unos minutos."),
        _ => (StatusCodes.Status500InternalServerError, IntegrationErrorCodes.InternalError,
            "Error interno del Portal Administrativo. Reintentá en unos minutos.")
    };

    public static (string ErrorCode, string Message, string Reason)? ForEmptyStatus(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => (IntegrationErrorCodes.Unauthorized,
            "Falta el token de sesión o ya no es válido. Iniciá sesión de nuevo.", "Token ausente, vencido o revocado."),
        StatusCodes.Status403Forbidden => (IntegrationErrorCodes.Forbidden,
            "Tu rol no tiene permiso para usar este servicio.", "El rol del usuario no tiene permiso para el endpoint."),
        StatusCodes.Status405MethodNotAllowed => (IntegrationErrorCodes.MethodNotAllowed,
            "El método HTTP no está permitido en este endpoint.", "Método HTTP no permitido."),
        StatusCodes.Status415UnsupportedMediaType => (IntegrationErrorCodes.UnsupportedMediaType,
            "El cuerpo del pedido tiene que ser JSON (Content-Type: application/json).", "Content-Type no soportado."),
        StatusCodes.Status429TooManyRequests => (IntegrationErrorCodes.TooManyRequests,
            "El Portal Administrativo está atendiendo muchas consultas de integración. Reintentá en unos segundos.",
            "Se superó el límite de consultas de integración simultáneas."),
        _ => null
    };

    // Endpoint sin datos: la plantilla de la ruta ("POST api/integracion/v1/estado-deuda/dni/{dni}"), nunca la ruta
    // real con el DNI. Si no hay plantilla (la ruta no existe), la ruta con sus valores ocultos.
    public static string EndpointOf(HttpContext? context)
    {
        if (context is null)
        {
            return "(desconocido)";
        }
        string? template = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
        string route = template is not null ? template.TrimStart('/') : LogSanitizer.SanitizePath(context.Request.Path.Value).TrimStart('/');
        return $"{context.Request.Method} {route}";
    }

    public static void Log(ILogger logger, HttpContext? context, int status, string errorCode, string reason)
    {
        var level = status >= StatusCodes.Status500InternalServerError ? LogLevel.Error : LogLevel.Warning;
        logger.Log(level,
            "Error de integración {ErrorCode} ({Status}). Timestamp: {Timestamp:o}. Endpoint: {Endpoint}. Motivo: {Reason}",
            errorCode, status, DateTimeOffset.UtcNow, EndpointOf(context), LogSanitizer.Sanitize(reason));
    }

    // La excepción no se pasa al logger tal cual (su mensaje podría traer datos): va su tipo, su mensaje y la pila, sanitizados.
    public static void LogException(ILogger logger, HttpContext? context, int status, string errorCode, Exception exception)
    {
        logger.Log(LogLevel.Error,
            "Error de integración {ErrorCode} ({Status}). Timestamp: {Timestamp:o}. Endpoint: {Endpoint}. Motivo: {Reason}. Detalle: {Detail}",
            errorCode, status, DateTimeOffset.UtcNow, EndpointOf(context),
            LogSanitizer.Sanitize($"{exception.GetType().Name}: {exception.Message}"),
            LogSanitizer.Sanitize(exception.ToString()));
    }
}
