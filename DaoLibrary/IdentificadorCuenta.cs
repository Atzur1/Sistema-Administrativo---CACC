namespace DaoLibrary;

// Lo que una persona escribe para identificarse en el login o en "Olvidé mi contraseña": su DNI (el usuario
// definitivo) o, mientras dura la transición, el correo de la cuenta. Un DNI nunca contiene "@", así que no
// se confunden.
public readonly record struct IdentificadorCuenta(bool EsCorreo, string Valor)
{
    public const int LargoMinimoDni = 6;
    public const int LargoMaximoDni = 9;

    // Deja solo los dígitos: "30.111.222" y "30 111 222" son el mismo DNI. Null si no queda un DNI posible.
    public static string? NormalizarDni(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var digitos = new string(texto.Where(char.IsAsciiDigit).ToArray());
        // Todo lo que no sea dígito, punto, espacio o guion invalida el valor (una letra no es un DNI).
        bool soloFormatoDeDni = texto.All(c => char.IsAsciiDigit(c) || c is '.' or ' ' or '-');
        if (!soloFormatoDeDni) return null;
        return digitos.Length is >= LargoMinimoDni and <= LargoMaximoDni ? digitos : null;
    }

    // Null si no es ni un correo ni un DNI posible: no hay nada que buscar.
    public static IdentificadorCuenta? Interpretar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var limpio = texto.Trim();
        if (limpio.Contains('@')) return new IdentificadorCuenta(true, limpio.ToLowerInvariant());
        var dni = NormalizarDni(limpio);
        return dni is null ? null : new IdentificadorCuenta(false, dni);
    }
}
