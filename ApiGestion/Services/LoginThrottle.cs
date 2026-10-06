using System.Collections.Concurrent;

namespace ApiGestion.Services;

// Límite de contraseñas equivocadas POR CUENTA, además del límite por IP del login. El DNI no es secreto, así que
// alguien podría probar contraseñas contra el de un administrador desde muchas IP distintas; con esto la cuenta
// deja de aceptar intentos después de demasiados fallos seguidos, venga de donde venga el intento.
//
// El costo conocido de cualquier límite por cuenta: quien conozca un DNI puede bloquearlo a propósito durante la
// ventana. Se acepta porque el bloqueo dura minutos, no es permanente, y no revela si la cuenta existe: cuenta
// los fallos de cualquier identificador, exista o no.
//
// Vive en memoria: se reinicia con la API. Alcanza para el despliegue de una sola instancia que tiene hoy.
public sealed class LoginThrottle(TimeProvider clock)
{
    public const int MaximoFallos = 10;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);
    private const int LimiteDeClaves = 10_000;

    private readonly ConcurrentDictionary<string, (int Fallos, DateTimeOffset Desde)> _fallos = new();

    public bool Bloqueado(string clave)
    {
        if (!_fallos.TryGetValue(clave, out var estado)) return false;
        if (clock.GetUtcNow() - estado.Desde >= Ventana)
        {
            _fallos.TryRemove(clave, out _);
            return false;
        }
        return estado.Fallos >= MaximoFallos;
    }

    public void RegistrarFallo(string clave)
    {
        var ahora = clock.GetUtcNow();
        if (_fallos.Count >= LimiteDeClaves) Purgar(ahora);

        _fallos.AddOrUpdate(clave,
            _ => (1, ahora),
            (_, actual) => ahora - actual.Desde >= Ventana ? (1, ahora) : (actual.Fallos + 1, actual.Desde));
    }

    public void Limpiar(string clave) => _fallos.TryRemove(clave, out _);

    // Evita que probar identificadores al azar llene la memoria: se descartan los que ya vencieron.
    private void Purgar(DateTimeOffset ahora)
    {
        foreach (var (clave, estado) in _fallos)
            if (ahora - estado.Desde >= Ventana) _fallos.TryRemove(clave, out _);
    }
}
