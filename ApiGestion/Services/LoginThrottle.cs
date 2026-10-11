using System.Security.Cryptography;
using System.Text;
using DaoLibrary;

namespace ApiGestion.Services;

// Límite de contraseñas equivocadas POR CUENTA, además del límite por IP del login. El DNI no es secreto, así que
// alguien podría probar contraseñas contra el de un administrador desde muchas IP distintas; con esto la cuenta
// deja de aceptar intentos después de demasiados fallos seguidos, venga de donde venga el intento.
//
// El costo conocido de cualquier límite por cuenta: quien conozca un DNI puede bloquearlo a propósito durante la
// ventana. Se acepta porque el bloqueo dura minutos, no es permanente, y no revela si la cuenta existe: cuenta
// los fallos de cualquier identificador, exista o no.
//
// Los fallos se guardan en la base (IntentosLoginDao): un reinicio de la API ya no los borra. El identificador
// se guarda hasheado, nunca en claro.
public sealed class LoginThrottle(TimeProvider clock, IIntentosLoginStore store)
{
    public const int MaximoFallos = 10;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);

    public bool Bloqueado(string identificador)
    {
        var estado = store.Obtener(Clave(identificador));
        if (estado is null) return false;
        if (clock.GetUtcNow() - estado.Value.Desde >= Ventana) return false;
        return estado.Value.Fallos >= MaximoFallos;
    }

    public void RegistrarFallo(string identificador) => store.RegistrarFallo(Clave(identificador), clock.GetUtcNow(), Ventana);

    public void Limpiar(string identificador) => store.Limpiar(Clave(identificador));

    // SHA-256 en hexadecimal (64 caracteres): misma clave para el mismo identificador, sin guardarlo en claro.
    internal static string Clave(string identificador)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identificador.Trim().ToLowerInvariant())));
}
