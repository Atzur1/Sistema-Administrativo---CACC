using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace ApiGestion.Services;

// HU-090: un pedido que no pasa la validación (fuera de la API de integración, que tiene su propio contrato) responde
// 400 con { mensaje, errores } en español, igual que el resto de las respuestas del portal, para que cada pantalla
// pueda mostrar error.error.mensaje. Los errores que arma el framework en inglés (valor no convertible, JSON roto,
// dato obligatorio) se traducen y nunca muestran tipos internos como System.Nullable.
public static partial class RespuestaDeValidacion
{
    public const string MensajeGenerico = "Los datos enviados no son válidos.";
    public const string MensajeJsonInvalido = "El pedido no tiene un formato válido: revisá los datos e intentá de nuevo.";
    public const string MensajeSinCuerpo = "Faltan los datos del pedido.";

    public static IActionResult Crear(ActionContext context) => new BadRequestObjectResult(Cuerpo(context.ModelState));

    public static RespuestaValidacion Cuerpo(ModelStateDictionary modelState)
    {
        var errores = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (clave, entrada) in modelState)
        {
            if (entrada.Errors.Count == 0) continue;
            string campo = NombreDeCampo(clave);
            errores[campo] = entrada.Errors.Select(error => Traducir(clave, campo, error)).Distinct().ToArray();
        }

        var mensajes = errores.Values.SelectMany(m => m).Distinct().ToList();
        // Un JSON roto suele arrastrar otros errores del mismo pedido: con el primero alcanza.
        string mensaje = mensajes.Contains(MensajeJsonInvalido)
            ? MensajeJsonInvalido
            : mensajes.Count > 0 ? string.Join(" ", mensajes) : MensajeGenerico;
        return new RespuestaValidacion(mensaje, errores);
    }

    private static string Traducir(string clave, string campo, ModelError error)
    {
        string texto = !string.IsNullOrWhiteSpace(error.ErrorMessage)
            ? error.ErrorMessage
            : error.Exception?.Message ?? string.Empty;

        // Errores del lector de JSON: la clave es una ruta ("$", "$.percentage", "$.items[0].monto").
        if (clave.StartsWith('$') || error.Exception is JsonException)
        {
            if (texto.StartsWith("The JSON value could not be converted", StringComparison.Ordinal) && campo.Length > 0)
                return $"El dato «{campo}» no tiene un formato válido.";
            return MensajeJsonInvalido;
        }

        if (texto.StartsWith("A non-empty request body is required", StringComparison.Ordinal))
            return MensajeSinCuerpo;

        var obligatorio = CampoObligatorio().Match(texto);
        if (obligatorio.Success)
            return NombreDeCampo(clave).Length == 0 || EsParametroDelCuerpo(clave)
                ? MensajeSinCuerpo
                : $"Falta el dato «{NombreDeCampo(clave)}».";

        var valorInvalido = ValorNoValido().Match(texto);
        if (valorInvalido.Success)
            return $"El valor «{valorInvalido.Groups[1].Value}» no es válido para «{campo}».";

        if (texto.StartsWith("The value '' is invalid", StringComparison.Ordinal))
            return $"Falta el dato «{campo}».";

        // Los mensajes propios de los DTO ya están en español: se devuelven tal cual. Uno en inglés que no se
        // reconoce no se muestra (puede nombrar tipos internos): se reemplaza por uno genérico del campo.
        return PareceIngles(texto)
            ? (campo.Length > 0 ? $"El dato «{campo}» no es válido." : MensajeGenerico)
            : texto;
    }

    // "$.percentage" -> "percentage"; "dto.Reason" -> "Reason"; "$" o "" -> "".
    internal static string NombreDeCampo(string clave)
    {
        string sinRaiz = clave.StartsWith('$') ? clave.TrimStart('$').TrimStart('.') : clave;
        int punto = sinRaiz.IndexOf('.');
        return !clave.StartsWith('$') && punto >= 0 ? sinRaiz[(punto + 1)..] : sinRaiz;
    }

    // La clave del parámetro completo del cuerpo ("dto", "request") no lleva punto ni corchetes.
    private static bool EsParametroDelCuerpo(string clave) => !clave.Contains('.') && !clave.Contains('[') && clave.Length > 0
        && char.IsLower(clave[0]);

    private static bool PareceIngles(string texto) =>
        texto.StartsWith("The ", StringComparison.Ordinal)
        || texto.StartsWith("A ", StringComparison.Ordinal)
        || texto.StartsWith("An ", StringComparison.Ordinal)
        || texto.Contains("System.", StringComparison.Ordinal)
        || texto.Contains(" is required", StringComparison.Ordinal)
        || texto.Contains(" must be ", StringComparison.Ordinal);

    [GeneratedRegex(@"^The (.+) field is required\.$")]
    private static partial Regex CampoObligatorio();

    [GeneratedRegex(@"^The value '(.+)' is not valid for (.+)\.$")]
    private static partial Regex ValorNoValido();
}

public sealed record RespuestaValidacion(string Mensaje, IReadOnlyDictionary<string, string[]> Errores);
