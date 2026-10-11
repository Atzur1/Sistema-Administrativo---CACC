namespace DaoLibrary
{
    // Dónde se guardan los intentos fallidos de ingreso por cuenta (LoginThrottle). La clave ya llega hasheada.
    public interface IIntentosLoginStore
    {
        // Fallos acumulados y desde cuándo se cuentan, o null si la clave no tiene fallos registrados.
        (int Fallos, DateTimeOffset Desde)? Obtener(string clave);

        // Suma un fallo. Si los anteriores ya salieron de la ventana, vuelve a contar desde 1 a partir de ahora.
        void RegistrarFallo(string clave, DateTimeOffset ahora, TimeSpan ventana);

        void Limpiar(string clave);
    }
}
