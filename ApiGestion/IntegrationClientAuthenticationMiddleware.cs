using ApiGestion.Services;
using DaoLibrary;

namespace ApiGestion;

// HU-055: solo el backend del Portal Deportivo puede llamar a la API de integración. Se monta únicamente para
// /api/integracion/... (y el alias /api/estado-deuda), antes de la autenticación de usuarios:
//  - sin X-Integration-Key, o con una que no es, responde 401 con el contrato { errorCode, message } y no llega al endpoint;
//  - con la credencial válida el pedido sigue igual que antes: el token del usuario, sus permisos y las reglas del endpoint
//    se validan como siempre. La credencial NO crea un usuario, ni roles, ni sesión: sola no alcanza para nada;
//  - mientras dura el pedido, las escrituras quedan auditadas con origen "PortalDeportivo" (SESSION_CONTEXT 'origen').
// En el log quedan el endpoint (sin valores), el resultado y el id de correlación; nunca la credencial.
public sealed class IntegrationClientAuthenticationMiddleware(
    RequestDelegate next, IntegrationClientCredentials credentials, ILogger<IntegrationClientAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var enviadas = context.Request.Headers[IntegrationClientCredentials.Header];
        if (enviadas.Count != 1 || !credentials.IsValid(enviadas[0]))
        {
            string motivo = enviadas.Count == 0 ? "Credencial de sistema ausente." : "Credencial de sistema inválida.";
            IntegrationErrors.Log(logger, context, StatusCodes.Status401Unauthorized, IntegrationErrorCodes.Unauthorized,
                motivo, context.TraceIdentifier);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(IntegrationErrors.Body(IntegrationErrorCodes.Unauthorized,
                "Falta la credencial del sistema que llama o no es válida."));
            return;
        }

        using (SqlAuditActorContext.PushSystem(IntegrationClientCredentials.SistemaDeportivo))
        {
            await next(context);
        }
    }
}
