using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiGestion.Services;

// HU-055: en el documento de integración, cada operación pide las dos cosas (credencial de sistema y token del usuario).
// En el del Portal Administrativo la credencial de sistema no sirve, salvo en las rutas que el middleware también protege con
// ella (el alias /api/estado-deuda): esas operaciones piden las dos, igual que en la implementación. La regla es la misma que
// usa el pipeline (IntegrationErrors.IsIntegrationRequest), así el documento no puede quedar distinto de lo que se exige.
public sealed class IntegrationKeySecurityFilter : IDocumentFilter
{
    private const string Esquema = "IntegrationKey";

    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        if (context.DocumentName == "integracion-v1")
        {
            document.SecurityRequirements = new List<OpenApiSecurityRequirement> { CredencialYToken() };
            return;
        }

        bool algunaConCredencial = false;
        foreach (var (ruta, item) in document.Paths)
        {
            if (!IntegrationErrors.IsIntegrationRequest(new PathString(ruta))) continue;
            foreach (var operacion in item.Operations.Values)
            {
                operacion.Security = new List<OpenApiSecurityRequirement> { CredencialYToken() };
            }
            algunaConCredencial = true;
        }
        if (!algunaConCredencial)
        {
            document.Components?.SecuritySchemes?.Remove(Esquema);
        }
    }

    private static OpenApiSecurityRequirement CredencialYToken() => new()
    {
        [Referencia(Esquema)] = Array.Empty<string>(),
        [Referencia("Bearer")] = Array.Empty<string>()
    };

    private static OpenApiSecurityScheme Referencia(string id) =>
        new() { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = id } };
}
