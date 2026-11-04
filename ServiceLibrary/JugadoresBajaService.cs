using System.Globalization;
using DaoLibrary;
using EntityLibrary;

namespace ServiceLibrary
{
    // YaEstabaDadoDeBaja: el pedido repetía una baja ya aplicada con la misma fecha (un reintento): no se cambió nada.
    public sealed record BajaIntegracionRegistrada(int IdJugador, DateTime FechaBaja, bool YaEstabaDadoDeBaja);

    // FechaAlta: la del reingreso (hoy). CategoriaCambiada: el pedido traía una categoría distinta de la que tenía.
    public sealed record ReincorporacionRegistrada(int IdJugador, DateTime FechaAlta, bool CategoriaCambiada);

    public interface IJugadoresBajaService
    {
        // HU-057: la baja que informa el Portal Deportivo. fechaBaja llega en crudo (yyyy-MM-dd). Es idempotente.
        ResultadoOperacion<BajaIntegracionRegistrada> DarDeBaja(int idJugador, string? fechaBaja);

        // HU-057: el reingreso de un jugador dado de baja, con la categoría que informa el Deportivo (null = la que tenía).
        ResultadoOperacion<ReincorporacionRegistrada> Reincorporar(int idJugador, int? idCategoria);
    }

    // Baja y reingreso pedidos por el Portal Deportivo (HU-057). Las reglas son las de la baja y la reactivación del Portal
    // Administrativo (JugadoresDao): este servicio solo valida el pedido y decide cómo responde cada caso.
    //  - Baja lógica: el jugador, la persona, sus pagos, su deuda y su auditoría quedan. Desde el mes siguiente al de la baja
    //    ningún camino de emisión le genera cuotas (SqlCuotaMensual.JugadorEnElClub es la única regla).
    //  - La fecha de baja es la efectiva que informa el Deportivo: no futura y no anterior a su permanencia en el club.
    //  - Reintento con la misma fecha = Ok sin cambios; con otra fecha = conflicto (no se pisa una baja ya registrada).
    //  - Reingreso: mismo jugador y misma persona; su fecha de alta pasa a ser hoy y la cuota del mes sigue el calendario
    //    (CalendarioCuotas): desde el día 10 se emite en el momento; antes, el 10 con el resto. Los meses de baja no se generan.
    public class JugadoresBajaService : IJugadoresBajaService
    {
        private readonly JugadoresDao _jugadoresDao;
        private readonly ICategoriasDao _categoriasDao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real del club.
        public JugadoresBajaService(JugadoresDao jugadoresDao, ICategoriasDao categoriasDao, Func<DateTime>? reloj = null)
        {
            _jugadoresDao = jugadoresDao;
            _categoriasDao = categoriasDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public ResultadoOperacion<BajaIntegracionRegistrada> DarDeBaja(int idJugador, string? fechaBaja)
        {
            if (idJugador <= 0)
            {
                return ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.Invalido, "Indicá un jugador válido.", "idJugador");
            }
            if (!DateTime.TryParseExact(fechaBaja?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime fecha))
            {
                return ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.Invalido,
                    "Indicá la fecha de baja con el formato yyyy-MM-dd.", "fechaBaja");
            }
            if (fecha > _hoy().Date)
            {
                return ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.Invalido,
                    "La fecha de baja no puede ser futura.", "fechaBaja");
            }

            ResultadoBajaJugador baja = _jugadoresDao.DarDeBaja(idJugador, fecha);
            return baja.Resultado switch
            {
                BajaJugadorResultado.Ok => ResultadoOperacion<BajaIntegracionRegistrada>.Ok(new(idJugador, fecha, false)),
                BajaJugadorResultado.YaDadoDeBaja when baja.FechaBajaVigente?.Date == fecha
                    => ResultadoOperacion<BajaIntegracionRegistrada>.Ok(new(idJugador, fecha, true)),
                BajaJugadorResultado.YaDadoDeBaja => ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.Conflicto,
                    $"El jugador ya está dado de baja desde el {baja.FechaBajaVigente:dd/MM/yyyy}: no se registra otra baja."),
                BajaJugadorResultado.FechaInvalida => ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.NoProcesable,
                    $"La fecha de baja no puede ser anterior al ingreso del jugador al club ({baja.InicioPermanencia:dd/MM/yyyy}).", "fechaBaja"),
                _ => ResultadoOperacion<BajaIntegracionRegistrada>.Falla(TipoResultado.NoEncontrado, $"No existe el jugador {idJugador}.")
            };
        }

        public ResultadoOperacion<ReincorporacionRegistrada> Reincorporar(int idJugador, int? idCategoria)
        {
            if (idJugador <= 0)
            {
                return ResultadoOperacion<ReincorporacionRegistrada>.Falla(TipoResultado.Invalido, "Indicá un jugador válido.", "idJugador");
            }
            if (idCategoria is <= 0)
            {
                return ResultadoOperacion<ReincorporacionRegistrada>.Falla(TipoResultado.Invalido, "Indicá una categoría válida.", "idCategoria");
            }
            if (idCategoria.HasValue && !_categoriasDao.ObtenerTodas().Any(c => c.IdCategoria == idCategoria.Value))
            {
                return ResultadoOperacion<ReincorporacionRegistrada>.Falla(TipoResultado.NoProcesable,
                    $"No existe la categoría con id {idCategoria}.", "idCategoria");
            }

            DateTime hoy = _hoy().Date;
            ResultadoReincorporacion reingreso = _jugadoresDao.Reincorporar(idJugador, idCategoria, hoy);
            return reingreso.Resultado switch
            {
                ReincorporacionResultado.Reincorporado
                    => ResultadoOperacion<ReincorporacionRegistrada>.Ok(new(idJugador, hoy, reingreso.CategoriaCambiada)),
                ReincorporacionResultado.YaActivo => ResultadoOperacion<ReincorporacionRegistrada>.Falla(TipoResultado.Conflicto,
                    "El jugador ya está activo: no hay nada que reincorporar."),
                _ => ResultadoOperacion<ReincorporacionRegistrada>.Falla(TipoResultado.NoEncontrado, $"No existe el jugador {idJugador}.")
            };
        }
    }
}
