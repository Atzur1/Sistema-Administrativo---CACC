using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ApiGestion.Services;

// [ApiController] convierte los errores sin cuerpo que arma MVC (415 por el Content-Type, un NotFound() vacío) en
// ProblemDetails. En la API de integración tienen que salir con el contrato { errorCode, message } (HU-058); en el resto
// de la API se sigue respondiendo ProblemDetails, igual que la fábrica por defecto del framework.
public sealed class IntegrationClientErrorFactory(ProblemDetailsFactory problemDetailsFactory, ILoggerFactory loggerFactory) : IClientErrorFactory
{
    public IActionResult GetClientError(ActionContext actionContext, IClientErrorActionResult clientError)
    {
        int status = clientError.StatusCode ?? StatusCodes.Status500InternalServerError;
        if (IntegrationErrors.IsIntegrationRequest(actionContext.HttpContext.Request.Path)
            && IntegrationErrors.ForEmptyStatus(status) is { } error)
        {
            IntegrationErrors.Log(loggerFactory.CreateLogger("ApiGestion.Integracion"), actionContext.HttpContext, status, error.ErrorCode, error.Reason);
            return new ObjectResult(IntegrationErrors.Body(error.ErrorCode, error.Message)) { StatusCode = status };
        }

        var problemDetails = problemDetailsFactory.CreateProblemDetails(actionContext.HttpContext, status);
        return new ObjectResult(problemDetails)
        {
            StatusCode = problemDetails.Status,
            ContentTypes = { "application/problem+json", "application/problem+xml" }
        };
    }
}
