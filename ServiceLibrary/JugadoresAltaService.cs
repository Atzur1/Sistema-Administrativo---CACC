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
                    FirstName = solicitud.Nombre.Trim(),
                    LastName = solicitud.Apellido.Trim(),
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

        private static ResultadoOperacion<AltaJugadorRegistrada> DniRepetido(string dni)
            => ResultadoOperacion<AltaJugadorRegistrada>.Falla(TipoResultado.Invalido, $"Ya hay una persona registrada con el DNI {dni}.");
    }
}
