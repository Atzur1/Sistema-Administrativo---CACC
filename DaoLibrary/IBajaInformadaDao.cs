namespace DaoLibrary
{
    // La baja que informa el Portal Deportivo (contrato de integración v1). Aparte de IJugadoresDao para no tocar a quienes
    // solo leen jugadores.
    public interface IBajaInformadaDao
    {
        // Ok con lo que se quitó (cuotas futuras sin pagos) y cancelló (becas), o NoEncontrado. Si el jugador no figuraba dado
        // de baja, la registra con la fecha de hoy; si ya figuraba (porque el Deportivo la grabó directo), solo aplica sus efectos.
        ResultadoBajaJugador AplicarBajaInformada(int idJugador);

        // Completa las bajas que quedaron a medias (por ejemplo, el Administrativo estaba caído cuando el Deportivo avisó, o la
        // baja se grabó directo en la base): a todo jugador dado de baja que todavía tenga cuotas futuras sin pagos o becas
        // vigentes le aplica los efectos de la baja. Idempotente. Devuelve a cuántos jugadores les completó algo.
        int CompletarBajasPendientes();
    }
}
