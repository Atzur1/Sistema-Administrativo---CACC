using Microsoft.AspNetCore.Mvc;
using ServiceLibrary;

namespace ApiGestion.Controllers;

// Traduce el resultado de una operación de negocio (ServiceLibrary) a la respuesta HTTP. Las fallas viajan como
// { exito: false, mensaje } con el mensaje en español, el mismo formato que usa toda la API.
internal static class ResultadoHttp
{
    public static IActionResult Responder<T>(this ControllerBase controller, ResultadoOperacion<T> resultado, Func<T, IActionResult> siSalioBien)
    {
        if (resultado.Exito)
        {
            return siSalioBien(resultado.Valor!);
        }

        var cuerpo = new { exito = false, mensaje = resultado.Mensaje };
        return resultado.Tipo switch
        {
            TipoResultado.NoEncontrado => controller.NotFound(cuerpo),
            TipoResultado.Conflicto => controller.Conflict(cuerpo),
            TipoResultado.NoProcesable => controller.UnprocessableEntity(cuerpo),
            _ => controller.BadRequest(cuerpo)
        };
    }
}
