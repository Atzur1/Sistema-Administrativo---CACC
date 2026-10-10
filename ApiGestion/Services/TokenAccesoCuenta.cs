using System.Security.Cryptography;
using System.Text;

namespace ApiGestion.Services;

// Enlaces de activación y recuperación: el token viaja en el correo y en la base solo se guarda su hash.
public static class TokenAccesoCuenta
{
    // 48 bytes aleatorios en Base64 URL-safe (sin relleno) y su SHA-256 en hexadecimal.
    public static (string Token, string Hash) Generar()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return (token, Hash(token));
    }

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
