using DaoLibrary;

namespace ApiGestion;

public sealed class AuditActorMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        int? actorId = int.TryParse(context.User.FindFirst("idUsuario")?.Value, out var id)
            ? id
            : null;

        using (SqlAuditActorContext.Push(actorId))
        {
            await next(context);
        }
    }
}
