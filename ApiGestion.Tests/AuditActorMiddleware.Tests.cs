namespace ApiGestion.Tests;

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using ApiGestion;
using DaoLibrary;

// HU-068 (devolución de QA): quien queda como autor en la auditoría es quien hizo la request. El middleware toma el
// idUsuario del token y lo deja en SqlAuditActorContext mientras corre el endpoint; SqlConnectionFactory lo pasa a
// SESSION_CONTEXT y de ahí lo leen los triggers (ver AccountAccessDaoIntegrationTests para ese tramo).
public class AuditActorMiddlewareTests
{
    private static DefaultHttpContext ContextoDe(string? idUsuario)
    {
        var context = new DefaultHttpContext();
        if (idUsuario is not null)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("idUsuario", idUsuario) }, "Bearer"));
        }
        return context;
    }

    [Fact]
    public async Task DuringTheRequest_TheAuthorIsTheAuthenticatedUser()
    {
        int? autorDuranteElEndpoint = null;
        var middleware = new AuditActorMiddleware(_ =>
        {
            autorDuranteElEndpoint = SqlAuditActorContext.CurrentUserId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ContextoDe("7"));

        Assert.Equal(7, autorDuranteElEndpoint);
    }

    [Fact]
    public async Task AnAnonymousRequest_HasNoAuthor()
    {
        int? autorDuranteElEndpoint = 123;
        var middleware = new AuditActorMiddleware(_ =>
        {
            autorDuranteElEndpoint = SqlAuditActorContext.CurrentUserId;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ContextoDe(null));

        Assert.Null(autorDuranteElEndpoint);
    }

    [Fact]
    public async Task AfterTheRequest_TheAuthorIsCleared()
    {
        var middleware = new AuditActorMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(ContextoDe("7"));

        Assert.Null(SqlAuditActorContext.CurrentUserId);
    }
}
