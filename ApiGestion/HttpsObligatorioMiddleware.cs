using ApiGestion.Services;

namespace ApiGestion;

// Fuera de Development y Testing la API solo atiende pedidos HTTPS. Un pedido HTTP se RECHAZA (403), no se redirige: con una
// redirección el JWT y X-Integration-Key ya viajaron en claro y el cliente mal configurado (por ejemplo, el Deportivo con
// AdministrativoApi:BaseUrl en http) sigue funcionando sin que nadie lo note. Va antes de la credencial de integración y de la
// autenticación, así un pedido HTTP nunca llega a validarse ni a ejecutarse.
// Detrás de un proxy que termina TLS, "HTTPS" es lo que informa X-Forwarded-Proto, y solo si el salto viene de un proxy
// confiable (ProxyConfiable): un cliente externo no puede declararse HTTPS con ese header.
public sealed class HttpsObligatorioMiddleware(RequestDelegate next, ILogger<HttpsObligatorioMiddleware> logger)
{
    public const string Mensaje = "La API solo acepta conexiones HTTPS.";

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.IsHttps)
        {
            await next(context);
            return;
        }

        bool integracion = IntegrationErrors.IsIntegrationRequest(context.Request.Path);
        // Sin la ruta: puede llevar un DNI (/estado-deuda/dni/{dni}). Nunca headers ni credenciales.
        logger.LogWarning("Pedido rechazado por no usar HTTPS ({Metodo}, integración: {Integracion}).", context.Request.Method, integracion);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        if (integracion)
            await context.Response.WriteAsJsonAsync(IntegrationErrors.Body(IntegrationErrorCodes.Forbidden, Mensaje));
        else
            await context.Response.WriteAsJsonAsync(new { exito = false, mensaje = Mensaje });
    }
}
