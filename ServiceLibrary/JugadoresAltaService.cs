using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using System.Globalization;

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

        // HU-056: el alta que pide el Portal Deportivo (persona + jugador + cargos, todo o nada). Es idempotente.
        // correlacion: el id del pedido, para el registro de rechazos.
        ResultadoOperacion<AltaIntegracionRegistrada> AltaDesdeIntegracion(SolicitudAltaIntegracion solicitud, string? correlacion = null);

        // HU-056: cambio de categoría pedido por el Portal Deportivo. No toca ningún cargo ya emitido.
        ResultadoOperacion<CambioCategoriaRealizado> CambiarCategoria(int idJugador, int? idCategoria, string? correlacion = null);
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
        private readonly IRegistroRechazos? _rechazos;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public JugadoresAltaService(PlayerDAO playerDao, ICategoriasDao categoriasDao, EnrollmentFeeDAO enrollmentFeeDao,
            IArancelesDao arancelesDao, Func<DateTime>? reloj = null, IRegistroRechazos? rechazos = null)
        {
            _playerDao = playerDao;
            _categoriasDao = categoriasDao;
            _enrollmentFeeDao = enrollmentFeeDao;
            _arancelesDao = arancelesDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
            _rechazos = rechazos;
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

        public ResultadoOperacion<AltaIntegracionRegistrada> AltaDesdeIntegracion(SolicitudAltaIntegracion solicitud, string? correlacion = null)
        {
            DateTime hoy = _hoy().Date;
            string dni = solicitud.Dni?.Trim() ?? "";
            // La misma regla que el alta del portal (PlayerRequestDTO) y que valida el Deportivo antes de llamar: 7 u 8 dígitos.
            // El 6 a 9 de PERSONAS y del login es el rango de identidades que la base admite, no el de un jugador nuevo.
            if (dni.Length is < 7 or > 8 || dni.Any(c => !char.IsAsciiDigit(c)))
            {
                return RechazarAlta(TipoResultado.Invalido, "Indicá un DNI válido de 7 u 8 dígitos.", "dni", correlacion);
            }

            string? genero = EntityLibrary.Generos.Validos.FirstOrDefault(g =>
                string.Equals(g, solicitud.Genero?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (genero is null)
            {
                return RechazarAlta(TipoResultado.Invalido, "Indicá un género válido.", "genero", correlacion);
            }

            if (solicitud.IdCategoria is not > 0)
            {
                return RechazarAlta(TipoResultado.Invalido, "Indicá una categoría válida.", "idCategoria", correlacion);
            }

            int idCategoria = solicitud.IdCategoria.Value;
            if (!_categoriasDao.ObtenerTodas().Any(c => c.IdCategoria == idCategoria))
            {
                return RechazarAlta(TipoResultado.NoProcesable, "La categoría indicada no existe.", "idCategoria", correlacion);
            }

            // Regla (decidida el 29/10/2026): "no futura" es respecto de la fecha REAL del club (RelojNegocio.Hoy), no de la
            // fecha de negocio del alta. En producción son la misma; solo difieren con Pruebas:FechaNegocio, que fija el
            // calendario de cargos pero no cambia qué nacimientos existen.
            DateTime? fechaNacimiento = null;
            if (!string.IsNullOrWhiteSpace(solicitud.FechaNacimiento))
            {
                if (!DateTime.TryParseExact(solicitud.FechaNacimiento.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime nacimiento) || nacimiento.Date > RelojNegocio.Hoy.Date)
                {
                    return RechazarAlta(TipoResultado.Invalido, "Indicá una fecha de nacimiento válida que no sea futura.",
                        "fechaNacimiento", correlacion);
                }
                fechaNacimiento = nacimiento.Date;
            }

            DatosAltaIntegracion datos = new(
                dni,
                NombreNormalizado(solicitud.Nombre),
                NombreNormalizado(solicitud.Apellido),
                genero,
                fechaNacimiento,
                idCategoria,
                hoy);

            ResultadoAltaIntegracion resultado = _playerDao.AltaDesdeIntegracion(datos, contexto =>
            {
                DateTime primerDiaDelMes = CalendarioCuotas.PrimerDiaDelMes(hoy);
                decimal? montoCuota = null;
                decimal? montoInscripcion = null;

                if (!contexto.JugadorNuevo && contexto.FechaAlta < hoy.AddDays(-DiasParaGenerarCargosDeAlta))
                {
                    return new PlanCargosAlta(null, primerDiaDelMes, null);
                }

                EnrollmentFee? inscripcion = _enrollmentFeeDao.GetCurrentEnrollmentFee(contexto.Genero);
                if (inscripcion is { Amount: > 0 })
                {
                    montoInscripcion = inscripcion.Amount;
                }
                else if (contexto.JugadorNuevo && contexto.Genero == Generos.Masculino)
                {
                    return new PlanCargosAlta(null, primerDiaDelMes, null,
                        "No hay un arancel de inscripción vigente para el alta.");
                }

                if (CalendarioCuotas.YaSeGeneraElMesEnCurso(hoy))
                {
                    DateTime ultimoDiaDelMes = primerDiaDelMes.AddMonths(1).AddDays(-1);
                    montoCuota = _arancelesDao.ObtenerMontoVigente(contexto.Genero, contexto.IdCategoria, ultimoDiaDelMes);
                }

                return new PlanCargosAlta(montoCuota, primerDiaDelMes, montoInscripcion);
            });

            return resultado.Estado switch
            {
                EstadoAltaIntegracion.Creado or EstadoAltaIntegracion.YaExistia =>
                    ResultadoOperacion<AltaIntegracionRegistrada>.Ok(new AltaIntegracionRegistrada(
                        resultado.IdJugador,
                        resultado.Estado == EstadoAltaIntegracion.Creado,
                        resultado.PersonaExistente,
                        resultado.Cargos.Inscripcion,
                        resultado.Cargos.Cuota)),
                EstadoAltaIntegracion.FaltanDatosDePersona =>
                    RechazarAlta(TipoResultado.Invalido, MensajeCampoPersona(resultado.Campo), resultado.Campo, correlacion,
                        resultado.IdPersona, resultado.IdJugador),
                EstadoAltaIntegracion.GeneroNoCoincide =>
                    RechazarAlta(TipoResultado.Conflicto, "El género no coincide con el de la persona existente.",
                        null, correlacion, resultado.IdPersona, resultado.IdJugador),
                EstadoAltaIntegracion.VariosJugadores =>
                    RechazarAlta(TipoResultado.Conflicto, "La persona tiene más de un jugador asociado; requiere revisión manual.",
                        null, correlacion, resultado.IdPersona, resultado.IdJugador),
                EstadoAltaIntegracion.VariasPersonas =>
                    RechazarAlta(TipoResultado.Conflicto, "Hay más de una persona con ese DNI; requiere revisión manual.",
                        null, correlacion),
                EstadoAltaIntegracion.JugadorDeBaja =>
                    RechazarAlta(TipoResultado.Conflicto, "La persona ya tiene un jugador dado de baja.",
                        null, correlacion, resultado.IdPersona, resultado.IdJugador),
                EstadoAltaIntegracion.CargosRechazados =>
                    RechazarAlta(TipoResultado.NoProcesable, resultado.Mensaje ?? "No se pudo determinar el cargo de inscripción.",
                        null, correlacion, resultado.IdPersona, resultado.IdJugador),
                _ => throw new InvalidOperationException($"Estado de alta integrada no reconocido: {resultado.Estado}.")
            };
        }

        public ResultadoOperacion<CambioCategoriaRealizado> CambiarCategoria(int idJugador, int? idCategoria, string? correlacion = null)
        {
            if (idJugador <= 0)
            {
                return RechazarCambioCategoria(TipoResultado.Invalido, "Indicá un jugador válido.", "idJugador", correlacion);
            }
            if (idCategoria is not > 0)
            {
                return RechazarCambioCategoria(TipoResultado.Invalido, "Indicá una categoría válida.", "idCategoria", correlacion,
                    idJugador: idJugador);
            }
            if (!_categoriasDao.ObtenerTodas().Any(c => c.IdCategoria == idCategoria.Value))
            {
                return RechazarCambioCategoria(TipoResultado.NoProcesable, "La categoría indicada no existe.", "idCategoria",
                    correlacion, idJugador: idJugador);
            }

            ResultadoCambioCategoria resultado = _playerDao.CambiarCategoria(idJugador, idCategoria.Value);
            return resultado.Estado switch
            {
                EstadoCambioCategoria.Cambiado or EstadoCambioCategoria.SinCambio =>
                    ResultadoOperacion<CambioCategoriaRealizado>.Ok(new CambioCategoriaRealizado(
                        idJugador, resultado.IdCategoria, resultado.Estado == EstadoCambioCategoria.Cambiado)),
                EstadoCambioCategoria.NoEncontrado =>
                    RechazarCambioCategoria(TipoResultado.NoEncontrado, $"No existe el jugador {idJugador}.", null,
                        correlacion, idJugador: idJugador),
                EstadoCambioCategoria.DeBaja =>
                    RechazarCambioCategoria(TipoResultado.NoProcesable, "No se puede cambiar la categoría de un jugador dado de baja.",
                        null, correlacion, idJugador: idJugador),
                _ => throw new InvalidOperationException($"Estado de cambio de categoría no reconocido: {resultado.Estado}.")
            };
        }

        private ResultadoOperacion<AltaIntegracionRegistrada> RechazarAlta(TipoResultado tipo, string mensaje, string? campo,
            string? correlacion, int? idPersona = null, int? idJugador = null)
        {
            RegistrarRechazo(OperacionesDeIntegracion.AltaJugador, tipo, mensaje, correlacion, idPersona, idJugador);
            return ResultadoOperacion<AltaIntegracionRegistrada>.Falla(tipo, mensaje, campo);
        }

        private ResultadoOperacion<CambioCategoriaRealizado> RechazarCambioCategoria(TipoResultado tipo, string mensaje,
            string? campo, string? correlacion, int? idJugador = null)
        {
            RegistrarRechazo(OperacionesDeIntegracion.CambioCategoria, tipo, mensaje, correlacion, null, idJugador);
            return ResultadoOperacion<CambioCategoriaRealizado>.Falla(tipo, mensaje, campo);
        }

        private void RegistrarRechazo(string operacion, TipoResultado tipo, string motivo, string? correlacion,
            int? idPersona, int? idJugador)
        {
            _rechazos?.Registrar(operacion, tipo, motivo, correlacion, idPersona, idJugador);
        }

        private static string? NombreNormalizado(string? nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return null;
            return NombresPropios.Normalizar(nombre.Trim());
        }

        private static string MensajeCampoPersona(string? campo) => campo switch
        {
            "nombre" => "Indicá el nombre de la persona.",
            "apellido" => "Indicá el apellido de la persona.",
            "fechaNacimiento" => "Indicá la fecha de nacimiento de la persona.",
            _ => "Faltan datos de la persona."
        };

        private static ResultadoOperacion<AltaJugadorRegistrada> DniRepetido(string dni)
            => ResultadoOperacion<AltaJugadorRegistrada>.Falla(TipoResultado.Invalido, $"Ya hay una persona registrada con el DNI {dni}.");
    }
}
