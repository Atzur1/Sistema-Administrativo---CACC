using ApiGestion.Services;

namespace ApiGestion;

// HU-058: red de contención de la API de integración con el Portal Deportivo. Se monta solo para /api/integracion/...
// (y el alias /api/estado-deuda), así que no cambia nada del resto del Portal Administrativo.
//  - Una excepción que se escape de un endpoint de integración termina en una respuesta controlada (500 o 503) con el
//    contrato { errorCode, message }, sin detalles internos, y se registra sanitizada. No llega al manejador global.
//  - Las respuestas vacías que arma el framework (401/403 de la autenticación, 429 del límite de consultas, 405, 415)
//    reciben el mismo contrato y quedan en el log.
//  - El 404 vacío se deja como está: para el Deportivo significa "la ruta no existe" (ver docs/integracion/README.md).
public sealed class IntegrationErrorHandlingMiddleware(RequestDelegate next, ILogger<IntegrationErrorHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // El Deportivo cortó la conexión: no hay a quién responderle.
            logger.LogInformation("Pedido de integración cancelado por el cliente. Endpoint: {Endpoint}", IntegrationErrors.EndpointOf(context));
            return;
        }
        catch (Exception exception) when (!context.Response.HasStarted)
        {
            var (status, errorCode, message) = IntegrationErrors.Classify(exception);
            IntegrationErrors.LogException(logger, context, status, errorCode, exception);
            // Lo que el endpoint haya dejado a medio armar (headers, estado) no se manda.
            context.Response.Clear();
            await WriteAsync(context, status, IntegrationErrors.Body(errorCode, message));
            return;
        }

        if (context.Response.HasStarted || context.Response.ContentType is not null || context.Response.ContentLength is > 0)
        {
            return;
        }
        if (IntegrationErrors.ForEmptyStatus(context.Response.StatusCode) is { } error)
        {
            IntegrationErrors.Log(logger, context, context.Response.StatusCode, error.ErrorCode, error.Reason);
            await WriteAsync(context, context.Response.StatusCode, IntegrationErrors.Body(error.ErrorCode, error.Message));
        }
    }

    // Sin Clear: en un 401 se conserva el header WWW-Authenticate que puso la autenticación.
    private static Task WriteAsync(HttpContext context, int status, Models.IntegrationErrorResponseDTO body)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(body);
    }
}
