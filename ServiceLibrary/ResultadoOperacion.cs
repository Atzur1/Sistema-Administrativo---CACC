namespace ServiceLibrary
{
    // Cómo terminó una operación de negocio. Los servicios no conocen HTTP: el controlador traduce cada tipo a su
    // código (ver ApiGestion.Controllers.ResultadoHttp), y el mensaje ya viene en español para mostrar tal cual.
    public enum TipoResultado
    {
        Ok,
        Invalido,      // 400: datos que no cumplen una regla
        NoEncontrado,  // 404
        Conflicto,     // 409: choca con otro dato (superposición, carrera con otra operación)
        NoProcesable   // 422: la regla de negocio no lo permite (por ejemplo, fecha pasada o jugador dado de baja)
    }

    public sealed class ResultadoOperacion<T>
    {
        public TipoResultado Tipo { get; }
        public T? Valor { get; }
        public string? Mensaje { get; }
        // Dato de entrada al que se refiere la falla (el "field" del error de integración). Null si no aplica a uno solo.
        public string? Campo { get; }
        // HU-087: cada regla que no se cumplió, para mostrarlas todas juntas (por ejemplo, la política de contraseñas).
        // Vacía cuando la falla es una sola y ya la explica el mensaje.
        public IReadOnlyList<string> Problemas { get; }

        private ResultadoOperacion(TipoResultado tipo, T? valor, string? mensaje, string? campo = null, IReadOnlyList<string>? problemas = null)
        {
            Tipo = tipo;
            Valor = valor;
            Mensaje = mensaje;
            Campo = campo;
            Problemas = problemas ?? Array.Empty<string>();
        }

        public bool Exito => Tipo == TipoResultado.Ok;

        public static ResultadoOperacion<T> Ok(T valor) => new(TipoResultado.Ok, valor, null);

        public static ResultadoOperacion<T> Falla(TipoResultado tipo, string mensaje, string? campo = null) => new(tipo, default, mensaje, campo);

        public static ResultadoOperacion<T> Falla(TipoResultado tipo, string mensaje, IReadOnlyList<string> problemas) =>
            new(tipo, default, mensaje, problemas: problemas);
    }
}
