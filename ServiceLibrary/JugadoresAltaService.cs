using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;

namespace ServiceLibrary
{
    // Datos de un jugador nuevo, ya validados en su forma (DNI de 7 u 8 dígitos, nacimiento pasado, género válido).
    public sealed record SolicitudAltaJugador(
        string Nombre, string Apellido, string Dni, DateTime FechaNacimiento, string Genero, long IdCategoria);

    // Lo que dejó registrado el alta: el jugador y los cargos que se le generaron (null si no se le cobró).
    public sealed record AltaJugadorRegistrada(Player Jugador, decimal? MontoCuota, decimal? MontoInscripcion);

    public interface IJugadoresAltaService
    {
        ResultadoOperacion<AltaJugadorRegistrada> Registrar(SolicitudAltaJugador solicitud);

        // Genera los cargos de alta (inscripción y cuota del mes) de un jugador que YA existe, por ejemplo uno que dio
        // de alta el Portal Deportivo directamente en la base. Aplica las mismas reglas que Registrar y es idempotente.
        ResultadoOperacion<CargosGenerados> GenerarCargosDeAlta(int idJugador);
    }

    // Alta de un jugador (HU-033) y los cargos que genera:
    //  - Cuota del mes: solo si el alta es desde el día 10 (CalendarioCuotas), cuando la del mes ya se generó para
    //    todos. Un alta antes del 10 la recibe ese día con el resto, o la paga por adelantado. Sin arancel vigente
    //    (ni de su categoría ni de su género) no se cobra, igual que en el generador mensual.
    //  - Inscripción: la de su género. En la rama masculina es obligatoria: sin un arancel de inscripción vigente,
    //    no hay alta. Un género sin arancel vigente, o con uno de $0, no paga inscripción (hoy: las mujeres).
    // Los dos cargos quedan como deudas pendientes separadas, grabadas junto con el jugador (todo o nada).
    public class JugadoresAltaService : IJugadoresAltaService
    {
        private readonly PlayerDAO _playerDao;
        private readonly ICategoriasDao _categoriasDao;
        private readonly EnrollmentFeeDAO _enrollmentFeeDao;
        private readonly IArancelesDao _arancelesDao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public JugadoresAltaService(PlayerDAO playerDao, ICategoriasDao categoriasDao, EnrollmentFeeDAO enrollmentFeeDao,
            IArancelesDao arancelesDao, Func<DateTime>? reloj = null)
        {
            _playerDao = playerDao;
            _categoriasDao = categoriasDao;
            _enrollmentFeeDao = enrollmentFeeDao;
            _arancelesDao = arancelesDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public ResultadoOperacion<AltaJugadorRegistrada> Registrar(SolicitudAltaJugador solicitud)
        {
            string dni = solicitud.Dni.Trim();

            if (_playerDao.ExistsPersonByDni(dni)) return DniRepetido(dni);

            if (!_categoriasDao.ObtenerTodas().Any(c => c.IdCategoria == solicitud.IdCategoria))
            {
                return ResultadoOperacion<AltaJugadorRegistrada>.Falla(TipoResultado.Invalido,
                    $"No existe la categoría con id {solicitud.IdCategoria}.");
            }

            decimal? montoInscripcion = null;
            EnrollmentFee? inscripcion = _enrollmentFeeDao.GetCurrentEnrollmentFee(solicitud.Genero);
            if (inscripcion == null && solicitud.Genero == Generos.Masculino)
            {
                return ResultadoOperacion<AltaJugadorRegistrada>.Falla(TipoResultado.Invalido,
                    "No hay un arancel de inscripción vigente. Cargalo en Actualización de aranceles antes de dar de alta a un jugador de la rama masculina.");
            }
            if (inscripcion is { Amount: > 0 })
            {
                montoInscripcion = inscripcion.Amount;
            }

            DateTime fechaAlta = _hoy().Date;
            decimal? montoCuota = null;
            if (CalendarioCuotas.YaSeGeneraElMesEnCurso(fechaAlta))
            {
                // Un arancel cargado el 5 cubre todo el mes: se busca contra el último día del mes, como el generador.
                // El de su categoría gana si tiene uno vigente; si no, el de su género.
                DateTime ultimoDiaDelMes = CalendarioCuotas.PrimerDiaDelMes(fechaAlta).AddMonths(1).AddDays(-1);
                montoCuota = _arancelesDao.ObtenerMontoVigente(solicitud.Genero, (int)solicitud.IdCategoria, ultimoDiaDelMes);
            }

            try
            {
                Player creado = _playerDao.CreatePlayer(new Player
                {
                    FirstName = NombresPropios.Normalizar(solicitud.Nombre),
                    LastName = NombresPropios.Normalizar(solicitud.Apellido),
                    Dni = dni,
                    BirthDate = solicitud.FechaNacimiento.Date,
                    Gender = solicitud.Genero,
                    CategoryId = solicitud.IdCategoria,
                    JoinDate = fechaAlta
                }, montoCuota, montoInscripcion);

                return ResultadoOperacion<AltaJugadorRegistrada>.Ok(new AltaJugadorRegistrada(creado, montoCuota, montoInscripcion));
            }
            catch (DniDuplicadoException)
            {
                // Otra alta con el mismo DNI ganó la carrera entre la verificación de arriba y la transacción.
                return DniRepetido(dni);
            }
        }

        // Solo para altas recientes: esta operación existe para completar un alta que se acaba de hacer en otro portal, no para
        // regularizar jugadores viejos (a quienes no se les debe generar una inscripción de alta hoy).
        public const int DiasParaGenerarCargosDeAlta = 7;

        public ResultadoOperacion<CargosGenerados> GenerarCargosDeAlta(int idJugador)
        {
            JugadorParaCargos? jugador = _playerDao.ObtenerParaCargos(idJugador);
            if (jugador == null)
            {
                return ResultadoOperacion<CargosGenerados>.Falla(TipoResultado.NoEncontrado, $"No existe el jugador {idJugador}.");
            }
            if (jugador.FechaBaja != null)
            {
                return ResultadoOperacion<CargosGenerados>.Falla(TipoResultado.NoProcesable, "El jugador está dado de baja: no se le generan cargos de alta.");
            }

            DateTime hoy = _hoy().Date;
            if (jugador.FechaAlta.Date < hoy.AddDays(-DiasParaGenerarCargosDeAlta))
            {
                return ResultadoOperacion<CargosGenerados>.Falla(TipoResultado.NoProcesable,
                    $"Solo se generan cargos de alta para jugadores dados de alta en los últimos {DiasParaGenerarCargosDeAlta} días.");
            }

            // La inscripción de su género; un género sin arancel vigente, o con uno de $0, no paga (hoy: las mujeres).
            decimal? montoInscripcion = null;
            EnrollmentFee? inscripcion = _enrollmentFeeDao.GetCurrentEnrollmentFee(jugador.Genero);
            if (inscripcion is { Amount: > 0 })
            {
                montoInscripcion = inscripcion.Amount;
            }

            // La cuota del mes solo desde el día 10 (antes la recibe con el resto en la generación mensual), con el arancel de
            // su categoría si tiene uno o, si no, el de su género; sin arancel vigente no se cobra.
            DateTime primerDiaDelMes = CalendarioCuotas.PrimerDiaDelMes(hoy);
            decimal? montoCuota = null;
            if (CalendarioCuotas.YaSeGeneraElMesEnCurso(hoy))
            {
                DateTime ultimoDiaDelMes = primerDiaDelMes.AddMonths(1).AddDays(-1);
                montoCuota = _arancelesDao.ObtenerMontoVigente(jugador.Genero, jugador.IdCategoria, ultimoDiaDelMes);
            }

            return ResultadoOperacion<CargosGenerados>.Ok(
                _playerDao.GenerarCargosDeAlta(idJugador, montoCuota, primerDiaDelMes, montoInscripcion, jugador.FechaAlta));
        }

        private static ResultadoOperacion<AltaJugadorRegistrada> DniRepetido(string dni)
            => ResultadoOperacion<AltaJugadorRegistrada>.Falla(TipoResultado.Invalido, $"Ya hay una persona registrada con el DNI {dni}.");
    }
}
