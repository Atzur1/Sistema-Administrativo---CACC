namespace EntityLibrary
{
    // El estado deportivo se calcula en un solo lugar (EstadoDeudaJugador.Estado) y se informa con estos textos en todos lados:
    // la API de integración con el Portal Deportivo y las pantallas del Administrativo (Deudas y Morosidad). Así no hay dos
    // implementaciones de la misma regla que puedan diferir.
    public static class EstadoDeportivoTexto
    {
        public const string Habilitado = "habilitado";
        public const string SoloEntrenamientos = "solo_entrenamientos";
        public const string Inhabilitado = "inhabilitado";
        public const string Baja = "baja";

        public static string De(EstadoDeportivo estado) => estado switch
        {
            EstadoDeportivo.Habilitado => Habilitado,
            EstadoDeportivo.SoloEntrenamientos => SoloEntrenamientos,
            EstadoDeportivo.Inhabilitado => Inhabilitado,
            _ => Baja
        };
    }
}
