namespace DaoLibrary
{
    public interface IBajasPendientesDao
    {
        // Completa las bajas que quedaron a medias: a todo jugador dado de baja que todavia tenga cuotas futuras sin pagos o becas
        // vigentes le aplica los efectos de la baja (los mismos de DarDeBaja). Idempotente. Devuelve a cuantos jugadores les completo algo.
        int CompletarBajasPendientes();
    }
}
