namespace ServiceLibrary
{
    // HU-056: el alta de jugadores que pide el Portal Deportivo. Llega en crudo: la validación (y el motivo de cada rechazo)
    // la hace el servicio, no el binding del controller, para que todo rechazo de negocio pase por el mismo lugar y quede
    // registrado.
    public sealed record SolicitudAltaIntegracion(
        string? Dni, string? Nombre, string? Apellido, string? Genero, string? FechaNacimiento, int? IdCategoria);

    // Qué quedó hecho. Nunca lleva montos: el Deportivo solo sabe SI se generó cada cargo.
    public sealed record AltaIntegracionRegistrada(
        int IdJugador, bool Creado, bool PersonaExistente, bool InscripcionGenerada, bool CuotaGenerada);

    public sealed record CambioCategoriaRealizado(int IdJugador, int IdCategoria, bool Cambiado);

    // Registra los pedidos de integración que se rechazaron por una regla. Nunca debe cambiar la respuesta: si no puede
    // registrar, lo deja en el log y sigue.
    public interface IRegistroRechazos
    {
        void Registrar(string operacion, TipoResultado tipo, string motivo, string? correlacion, int? idPersona = null, int? idJugador = null);
        void RegistrarModeloInvalido(string operacion, string motivo, string? correlacion, int? idJugador = null);
    }

    public static class OperacionesDeIntegracion
    {
        public const string AltaJugador = "AltaJugador";
        public const string CambioCategoria = "CambioCategoria";
    }
}
