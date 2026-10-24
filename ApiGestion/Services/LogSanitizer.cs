using System.Text.RegularExpressions;

namespace ApiGestion.Services;

// Lo que se escribe en el log de la integración pasa por acá: nunca se registran credenciales, tokens ni datos personales.
// Se aplica a todo texto que pueda traer datos de afuera (mensajes de excepción, rutas). Ante la duda se oculta de más.
public static partial class LogSanitizer
{
    // "Bearer eyJ..." en un header o un mensaje.
    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-_.~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerToken();

    // Un JWT suelto (tres partes base64url, la primera empieza con "eyJ").
    [GeneratedRegex(@"eyJ[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]+\.[A-Za-z0-9\-_]*")]
    private static partial Regex Jwt();

    // clave=valor o clave: valor de cualquier secreto (incluye las cadenas de conexión: "Password=...;").
    [GeneratedRegex(@"\b(password|pwd|contrase(?:ñ|n)a|contrasenia|secret|token|api[_-]?key|authorization)\b(\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s;,&""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignment();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex Email();

    // DNI con o sin puntos (41.000.001 / 41000001) y cualquier número largo que pueda identificar a alguien.
    [GeneratedRegex(@"\b\d{1,3}(?:\.\d{3}){2}\b|\b\d{6,}\b")]
    private static partial Regex PersonalNumber();

    // Segmentos de ruta con un valor (un DNI, un id): /dni/41000001 -> /dni/{valor}. La versión (v1) no es un valor.
    [GeneratedRegex(@"(?<=/)(?![vV]\d+(?:/|$))[^/]*\d[^/]*(?=/|$)")]
    private static partial Regex PathValueSegment();

    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }
        string result = BearerToken().Replace(text, "Bearer [oculto]");
        result = Jwt().Replace(result, "[token oculto]");
        result = SecretAssignment().Replace(result, m => $"{m.Groups[1].Value}{m.Groups[2].Value}[oculto]");
        result = Email().Replace(result, "[correo oculto]");
        return PersonalNumber().Replace(result, "[dato oculto]");
    }

    // La ruta pedida sin sus valores ni la query string. Se usa solo cuando no se conoce la plantilla de la ruta.
    public static string SanitizePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "/";
        }
        int query = path.IndexOf('?');
        string withoutQuery = query >= 0 ? path[..query] : path;
        return PathValueSegment().Replace(withoutQuery, "{valor}");
    }
}
