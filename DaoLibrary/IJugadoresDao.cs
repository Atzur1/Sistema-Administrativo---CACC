using EntityLibrary;

namespace DaoLibrary
{
    public interface IJugadoresDao
    {
        // Lista completa liviana: el frontend filtra client-side (mismo patrón que ya usaba con datos mock).
        IReadOnlyList<JugadorResumen> ListarJugadores();

        JugadorResumen? ObtenerJugadorPorId(int idJugador);

        // Baja desde esa fecha (deja de generar cuotas a partir del mes siguiente) y limpieza de las
        // cuotas pendientes sin pagos de meses posteriores. Ver JugadoresDao.DarDeBaja.
        (BajaJugadorResultado Resultado, int CuotasEliminadas) DarDeBaja(int idJugador, DateTime fechaBaja);

        // false si el jugador no existe o no estaba dado de baja.
        bool Reactivar(int idJugador);
    }
}
