using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApiGestion.Services;

// HU-090: errores que arma el framework antes de llegar al controller, con el mismo contrato que el resto del Portal
// Administrativo: { exito: false, mensaje } en español. Antes salían como ProblemDetails/ValidationProblemDetails
// ("One or more validation errors occurred.", "The JSON value could not be converted to…", nombres de propiedades),
// que la pantalla no podía mostrar. Los status no cambian: la validación sigue siendo 400.
// La API de integración con el Deportivo tiene su propio contrato (IntegrationErrors, HU-058) y no pasa por acá.
public static class ErroresDelPortal
{
    public const string MensajeGenerico = "Ocurrió un error al procesar la solicitud.";
    public const string MensajeSinDatos = "No se recibieron los datos del formulario o no tienen un formato válido.";
    public const string MensajeDatoInvalidoGenerico = "Uno de los datos enviados no tiene un formato válido.";

    // Cómo se llama para el usuario cada dato que viaja en el JSON. Si no está, el mensaje no nombra el dato: nunca se
    // muestra el nombre interno (FK_id_categoria, IdsPago…).
    private static readonly Dictionary<string, string> Etiquetas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reason"] = "motivo",
        ["valueType"] = "tipo de valor",
        ["percentage"] = "porcentaje",
        ["fixedAmount"] = "monto fijo",
        ["startDate"] = "fecha de inicio",
        ["endDate"] = "fecha de fin",
        ["firstName"] = "nombre",
        ["lastName"] = "apellido",
        ["nombre"] = "nombre",
        ["apellido"] = "apellido",
        ["dni"] = "DNI",
        ["birthDate"] = "fecha de nacimiento",
        ["gender"] = "género",
        ["genero"] = "género",
        ["categoryId"] = "categoría",
        ["idCategoria"] = "categoría",
        ["amount"] = "monto",
        ["monto"] = "monto",
        ["valor"] = "valor",
        ["tipo"] = "tipo de cambio",
        ["vigenteDesde"] = "fecha de vigencia",
        ["jugadoresConfirmados"] = "cantidad de jugadores",
        ["paymentMethod"] = "método de pago",
        ["metodoPago"] = "método de pago",
        ["idJugador"] = "jugador",
        ["periodo"] = "período",
        ["anio"] = "año",
        ["idsPago"] = "cuotas",
        ["motivo"] = "motivo",
        ["fecha"] = "fecha",
        ["email"] = "correo electrónico",
        ["rolPortal"] = "rol",
    };

    // Textos de binding de rutas y queries ("abc" como id, un valor faltante) en español y sin nombres de parámetros.
    public static void ConfigurarMensajesDeBinding(MvcOptions opciones)
    {
        var p = opciones.ModelBindingMessageProvider;
        p.SetAttemptedValueIsInvalidAccessor((_, _) => "Uno de los valores indicados no es válido.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Uno de los valores indicados no es válido.");
        p.SetUnknownValueIsInvalidAccessor(_ => "Uno de los valores indicados no es válido.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Uno de los valores indicados no es válido.");
        p.SetValueIsInvalidAccessor(_ => "Uno de los valores indicados no es válido.");
        p.SetValueMustBeANumberAccessor(_ => "Uno de los valores debe ser un número.");
        p.SetNonPropertyValueMustBeANumberAccessor(() => "Uno de los valores debe ser un número.");
        p.SetMissingBindRequiredValueAccessor(_ => "Falta un dato obligatorio.");
        p.SetMissingKeyOrValueAccessor(() => "Falta un dato obligatorio.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => MensajeSinDatos);
        p.SetValueMustNotBeNullAccessor(_ => "Falta un dato obligatorio.");
    }

    // 400 por validación del modelo. Junta en un solo mensaje todos los problemas (los de los DTO ya están en español),
    // y reemplaza los que genera el framework al leer el JSON.
    public static IActionResult RespuestaValidacion(ActionContext context)
    {
        HashSet<string> parametrosDelCuerpo = (context.ActionDescriptor as ControllerActionDescriptor)?.Parameters
            .Where(p => p.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var mensajes = new List<string>();
        foreach (var (clave, entrada) in context.ModelState)
        {
            foreach (ModelError error in entrada.Errors)
            {
                string mensaje = MensajeDe(clave, error, parametrosDelCuerpo);
                if (!mensajes.Contains(mensaje)) mensajes.Add(mensaje);
            }
        }

        return new BadRequestObjectResult(new { exito = false, mensaje = mensajes.Count > 0 ? string.Join(" ", mensajes) : MensajeSinDatos });
    }

    // Errores sin cuerpo que arma MVC (un NotFound() vacío, 415 por el Content-Type): mismo status, mensaje en español.
    public static object CuerpoSinDetalle(int status) => new
    {
        exito = false,
        mensaje = status switch
        {
            StatusCodes.Status401Unauthorized => "Tu sesión venció. Volvé a iniciar sesión.",
            StatusCodes.Status403Forbidden => "No tenés permisos para realizar esta acción.",
            StatusCodes.Status404NotFound => "No se encontró lo que buscabas.",
            StatusCodes.Status400BadRequest or StatusCodes.Status415UnsupportedMediaType => MensajeSinDatos,
            _ => MensajeGenerico
        }
    };

    private static string MensajeDe(string clave, ModelError error, HashSet<string> parametrosDelCuerpo)
    {
        // El cuerpo entero falta o no es JSON: "" / "$" / el nombre del parámetro [FromBody] ("request").
        if (clave.Length == 0 || clave == "$" || parametrosDelCuerpo.Contains(clave))
        {
            return MensajeSinDatos;
        }

        // Un dato del JSON que no se pudo leer ("$.percentage": el texto viene de System.Text.Json, en inglés).
        if (clave.StartsWith("$.", StringComparison.Ordinal) || error.Exception is not null || string.IsNullOrWhiteSpace(error.ErrorMessage))
        {
            string dato = clave.TrimStart('$', '.');
            int corchete = dato.IndexOf('[');
            if (corchete >= 0) dato = dato[..corchete];
            return Etiquetas.TryGetValue(dato, out string? etiqueta)
                ? $"El dato «{etiqueta}» no tiene un formato válido."
                : MensajeDatoInvalidoGenerico;
        }

        // Los mensajes de los DTO del portal (ErrorMessage / IValidatableObject), que ya están en español.
        return error.ErrorMessage;
    }
}
