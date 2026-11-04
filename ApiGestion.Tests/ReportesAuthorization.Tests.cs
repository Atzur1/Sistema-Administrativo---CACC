using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ApiGestion.Controllers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ApiGestion.Tests;

// Real MVC endpoint discovery + ASP.NET Core authentication/authorization pipeline.
// Only authentication is synthetic. After authorization, stop before SQL/controller execution.
// This checks HTTP 401/403/allowed against the production controller's actual endpoint metadata.
public class ReportesAuthorizationTests
{
    [Theory]
    [InlineData(null, "/api/reportes/historial", 401)]
    [InlineData("2", "/api/reportes/historial", 403)]
    [InlineData("1", "/api/reportes/historial", 204)]
    [InlineData(null, "/api/reportes/deudores/conteo", 401)]
    [InlineData("2", "/api/reportes/deudores/conteo", 204)]
    [InlineData("1", "/api/reportes/deudores/conteo", 204)]
    [InlineData("1", "/api/reportes/recaudacion/conteo?anio=2026&mes=10", 204)]
    [InlineData("2", "/api/reportes/deudores/csv", 204)]
    [InlineData("2", "/api/reportes/deudores/pdf", 204)]
    [InlineData("2", "/api/reportes/pagos-recientes/csv", 204)]
    [InlineData("2", "/api/reportes/becados/pdf", 204)]
    [InlineData("2", "/api/reportes/aranceles/csv", 403)]
    [InlineData("2", "/api/reportes/recaudacion/pdf?anio=2026&mes=10", 403)]
    [InlineData("1", "/api/reportes/aranceles/csv", 204)]
    public async Task LasRutasReales_AplicanLosPermisosEnHttp(string? rol, string ruta, int esperado)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
        builder.Services.AddControllers().AddApplicationPart(typeof(ReportesController).Assembly);
        builder.Services.AddAuthentication("Prueba").AddScheme<AuthenticationSchemeOptions, IdentidadPrueba>("Prueba", _ => { });
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint() is not null) context.Response.StatusCode = StatusCodes.Status204NoContent;
            else await next(context);
        });
        app.MapControllers();
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient { BaseAddress = new Uri(address) };
        if (rol is not null) client.DefaultRequestHeaders.Add("X-Test-Rol", rol);
        using var response = await client.GetAsync(ruta);
        Assert.Equal((HttpStatusCode)esperado, response.StatusCode);
    }

    public sealed class IdentidadPrueba(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-Rol", out var rol)) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, rol.ToString()), new Claim("idUsuario", "7")], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
