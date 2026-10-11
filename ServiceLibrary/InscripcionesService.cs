using DaoLibrary;
using EntityLibrary;

namespace ServiceLibrary
{
    public sealed record SolicitudPagoInscripcion(long IdJugador, decimal Monto, string MetodoPago, int IdUsuarioRegistro);

    public interface IInscripcionesService
    {
        Enrollment? ObtenerDelJugador(long idJugador);
        ResultadoOperacion<EnrollmentPayment> RegistrarPago(SolicitudPagoInscripcion solicitud);
    }

    // La inscripción de un jugador y sus pagos (HU-033): separada de las cuotas, con su propio saldo y pagos parciales.
    //  - Si el jugador todavía no tiene inscripción, se crea en el primer pago con el arancel de inscripción
    //    vigente: solo para la rama masculina y nunca para un jugador dado de baja.
    //  - Una inscripción que ya existe se puede cobrar siempre (también a un dado de baja: es deuda).
    //  - Nunca se cobra más que el saldo (ni que el arancel, en el primer pago).
    public class InscripcionesService : IInscripcionesService
    {
        private readonly EnrollmentDAO _enrollmentDao;
        private readonly EnrollmentFeeDAO _enrollmentFeeDao;
        private readonly IJugadoresDao _jugadoresDao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public InscripcionesService(EnrollmentDAO enrollmentDao, EnrollmentFeeDAO enrollmentFeeDao, IJugadoresDao jugadoresDao, Func<DateTime>? reloj = null)
        {
            _enrollmentDao = enrollmentDao;
            _enrollmentFeeDao = enrollmentFeeDao;
            _jugadoresDao = jugadoresDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public Enrollment? ObtenerDelJugador(long idJugador) => _enrollmentDao.GetEnrollmentByPlayerId(idJugador);

        public ResultadoOperacion<EnrollmentPayment> RegistrarPago(SolicitudPagoInscripcion solicitud)
        {
            long idJugador = solicitud.IdJugador;
            decimal monto = solicitud.Monto;
            string metodo = solicitud.MetodoPago.Trim();

            JugadorResumen? jugador = idJugador is > 0 and <= int.MaxValue ? _jugadoresDao.ObtenerJugadorPorId((int)idJugador) : null;
            if (jugador == null)
            {
                return Falla(TipoResultado.NoEncontrado, $"No existe el jugador #{idJugador}.");
            }

            Enrollment? inscripcion = _enrollmentDao.GetEnrollmentByPlayerId(idJugador);
            if (inscripcion == null)
            {
                return CrearConPrimerPago(jugador, solicitud, metodo);
            }

            if (inscripcion.PendingBalance == 0)
            {
                return Falla(TipoResultado.Invalido, "La inscripción de este jugador ya está totalmente pagada.");
            }
            if (monto > inscripcion.PendingBalance)
            {
                return Falla(TipoResultado.Invalido, $"El monto supera el saldo pendiente de la inscripción (${inscripcion.PendingBalance:N0}).");
            }

            EnrollmentPayment? pago = _enrollmentDao.CreateEnrollmentPayment(idJugador, monto, metodo, _hoy(), solicitud.IdUsuarioRegistro);
            return pago == null
                ? Falla(TipoResultado.Conflicto, "La inscripción cambió mientras se registraba el pago. Actualizá la pantalla y volvé a intentar.")
                : ResultadoOperacion<EnrollmentPayment>.Ok(pago);
        }

        // Jugador sin inscripción todavía (por ejemplo, dado de alta antes de HU-033): se crea con el arancel vigente.
        private ResultadoOperacion<EnrollmentPayment> CrearConPrimerPago(JugadorResumen jugador, SolicitudPagoInscripcion solicitud, string metodo)
        {
            if (jugador.Genero != Generos.Masculino)
            {
                return Falla(TipoResultado.Invalido, "La inscripción corresponde solo a la rama masculina: este jugador no tiene inscripción para cobrar.");
            }
            if (jugador.FechaBaja != null)
            {
                return Falla(TipoResultado.Invalido, $"El jugador está dado de baja desde el {jugador.FechaBaja:dd/MM/yyyy}: no se le genera una inscripción nueva.");
            }

            EnrollmentFee? arancel = _enrollmentFeeDao.GetCurrentEnrollmentFee();
            if (arancel == null)
            {
                return Falla(TipoResultado.Conflicto, "No hay un arancel de inscripción vigente. Cargalo en Actualización de aranceles antes de registrar el pago.");
            }
            if (solicitud.Monto > arancel.Amount)
            {
                return Falla(TipoResultado.Invalido, $"El monto supera el arancel de inscripción (${arancel.Amount:N0}).");
            }

            EnrollmentPayment? primerPago = _enrollmentDao.CreateEnrollmentAndFirstPayment(
                solicitud.IdJugador, arancel.Amount, solicitud.Monto, metodo, _hoy(), solicitud.IdUsuarioRegistro);
            return primerPago == null
                ? Falla(TipoResultado.Conflicto, "Otra operación creó la inscripción al mismo tiempo. Actualizá la pantalla y volvé a intentar.")
                : ResultadoOperacion<EnrollmentPayment>.Ok(primerPago);
        }

        private static ResultadoOperacion<EnrollmentPayment> Falla(TipoResultado tipo, string mensaje)
            => ResultadoOperacion<EnrollmentPayment>.Falla(tipo, mensaje);
    }
}
