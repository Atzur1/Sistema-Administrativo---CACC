using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace ApiGestion.Services;

// [ApiController] convierte los errores sin cuerpo que arma MVC (415 por el Content-Type, un NotFound() vacío) en
// ProblemDetails. En la API de integración tienen que salir con el contrato { errorCode, message } (HU-058); en el resto
// del portal, con { exito, mensaje } en español (HU-090).
public sealed class IntegrationClientErrorFactory(ILoggerFactory loggerFactory) : IClientErrorFactory
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

        // HU-090: en el resto del portal, el mismo status con { exito, mensaje } en español (antes, ProblemDetails con el
        // title del framework en inglés).
        return new ObjectResult(ErroresDelPortal.CuerpoSinDetalle(status)) { StatusCode = status };
    }
}
