using System.Security.Cryptography;
using System.Text;

namespace ApiGestion.Services;

// HU-055: credencial de sistema del Portal Deportivo para la API de integración. Es la identidad del SISTEMA que llama,
// no la de una persona: viaja en X-Integration-Key desde el backend del Deportivo, junto con el token del usuario que
// opera (HU-054). Se configura por entorno (user-secrets en Development, variables de entorno o secretos del despliegue)
// y nunca se versiona. ClaveAnterior es opcional y sirve solo mientras dura una rotación.
public sealed class IntegrationClientCredentials
{
    public const string Header = "X-Integration-Key";
    public const string SistemaDeportivo = "PortalDeportivo";
    public const string ConfigClave = "Integracion:ClienteDeportivo:Clave";
    public const string ConfigClaveAnterior = "Integracion:ClienteDeportivo:ClaveAnterior";
    private const int LargoMinimo = 32;

    // Se guarda el hash, no la clave: comparar dos hashes de 32 bytes con FixedTimeEquals tarda lo mismo sea cual sea
    // la clave enviada y su largo, así que la respuesta no deja adivinar la correcta.
    private readonly byte[][] _hashes;

    private IntegrationClientCredentials(byte[][] hashes) => _hashes = hashes;

    public static IntegrationClientCredentials FromConfiguration(IConfiguration configuration)
    {
        string actual = Validar(configuration[ConfigClave], ConfigClave)!;
        string? anterior = configuration[ConfigClaveAnterior];
        var claves = new List<string> { actual };
        // Vacía = no hay clave anterior (por ejemplo, la variable quedó definida pero sin valor al terminar una rotación).
        if (!string.IsNullOrWhiteSpace(anterior))
        {
            claves.Add(Validar(anterior, ConfigClaveAnterior)!);
        }
        return new IntegrationClientCredentials(claves.Select(Hash).ToArray());
    }

    public bool IsValid(string? enviada)
    {
        if (string.IsNullOrEmpty(enviada))
        {
            return false;
        }
        byte[] hash = Hash(enviada);
        bool valida = false;
        foreach (byte[] esperada in _hashes)
        {
            // Sin cortar al primer acierto: el tiempo no depende de cuál clave coincide.
            valida |= CryptographicOperations.FixedTimeEquals(hash, esperada);
        }
        return valida;
    }

    // El mensaje nombra la configuración que falta, nunca el valor.
    private static string? Validar(string? clave, string nombre)
    {
        if (string.IsNullOrWhiteSpace(clave) || clave.StartsWith("REEMPLAZAR", StringComparison.OrdinalIgnoreCase) ||
            Encoding.UTF8.GetByteCount(clave) < LargoMinimo)
        {
            throw new InvalidOperationException(
                $"La configuración {nombre} es obligatoria: una clave generada al azar de al menos {LargoMinimo} bytes " +
                "(user-secrets en Development, variable de entorno o secreto del despliegue). Ver database/InstructivoEquipo.md.");
        }
        // Kestrel recorta los espacios de los extremos de cada header, así que una clave que los tenga (un salto de línea
        // final de un archivo de secreto o de un .env) nunca coincidiría: mejor no arrancar que responder 401 a todo.
        if (clave != clave.Trim())
        {
            throw new InvalidOperationException(
                $"La configuración {nombre} tiene espacios o saltos de línea al principio o al final. Cargala sin ellos.");
        }
        return clave;
    }

    private static byte[] Hash(string clave) => SHA256.HashData(Encoding.UTF8.GetBytes(clave));
}
