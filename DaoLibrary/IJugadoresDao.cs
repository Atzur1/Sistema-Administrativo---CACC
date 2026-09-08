using EntityLibrary;

namespace DaoLibrary
{
    public interface IJugadoresDao
    {
        // Lista completa liviana: el frontend filtra client-side (mismo patrón que ya usaba con datos mock).
        IReadOnlyList<JugadorResumen> ListarJugadores();

        JugadorResumen? ObtenerJugadorPorId(int idJugador);
    }
}
