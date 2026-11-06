using DaoLibrary;

namespace ServiceLibrary
{
    // Lo que provocó la baja: cuántas cuotas futuras sin pagos se quitaron y cuántas becas se cancelaron. Nunca montos.
    public sealed record BajaAplicada(int CuotasFuturasQuitadas, int BeneficiosCancelados);

    public interface IJugadoresBajaService
    {
        // Aplica los efectos de la baja de un jugador que el Portal Deportivo dio de baja. Idempotente.
        ResultadoOperacion<BajaAplicada> AplicarBajaInformada(int idJugador);
    }

    // Baja informada por el Portal Deportivo: reutiliza la lógica de la baja del Administrativo (JugadoresDao.DarDeBaja):
    // quita solo las cuotas pendientes sin pagos de los meses posteriores a la baja, mantiene la deuda anterior y cancela las
    // becas vigentes o programadas.
    public class JugadoresBajaService : IJugadoresBajaService
    {
        private readonly IBajaInformadaDao _dao;

        public JugadoresBajaService(IBajaInformadaDao dao) => _dao = dao;

        public ResultadoOperacion<BajaAplicada> AplicarBajaInformada(int idJugador)
        {
            var baja = _dao.AplicarBajaInformada(idJugador);
            return baja.Resultado switch
            {
                BajaJugadorResultado.Ok => ResultadoOperacion<BajaAplicada>.Ok(new BajaAplicada(baja.CuotasEliminadas, baja.BeneficiosCancelados)),
                BajaJugadorResultado.NoEncontrado => ResultadoOperacion<BajaAplicada>.Falla(TipoResultado.NoEncontrado, $"No existe el jugador {idJugador}."),
                _ => ResultadoOperacion<BajaAplicada>.Falla(TipoResultado.NoProcesable,
                    "No se pudo aplicar la baja: la fecha de baja no puede ser anterior a la de alta del jugador.")
            };
        }
    }
}
