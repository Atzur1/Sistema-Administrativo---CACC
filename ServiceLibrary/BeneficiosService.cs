using DaoLibrary;
using EntityLibrary;

namespace ServiceLibrary
{
    // Lo que pide el formulario de Becados y Descuentos, ya validado en su forma (fechas parseadas, valor
    // coherente con el tipo). Las reglas de negocio las aplica BeneficiosService.
    public sealed record SolicitudBeneficio(
        string Motivo, string TipoValor, decimal? Porcentaje, decimal? MontoFijo, DateTime Inicio, DateTime Fin);

    public interface IBeneficiosService
    {
        IReadOnlyList<Discount> ObtenerVigentes();
        IReadOnlyList<Discount> ObtenerTodos();
        IReadOnlyList<string> ObtenerMotivos();

        // El beneficio con el que trabaja el formulario: el vigente, si no el próximo programado, si no el último vencido.
        Discount? ObtenerAsignado(long idJugador);
        IReadOnlyList<Discount> ObtenerDelJugador(long idJugador);

        ResultadoOperacion<Discount> Asignar(long idJugador, SolicitudBeneficio solicitud);
        ResultadoOperacion<Discount> Editar(long idJugador, long idBeneficio, SolicitudBeneficio solicitud);
        ResultadoOperacion<long> Cancelar(long idJugador, long idBeneficio);
        ResultadoOperacion<long> Anular(long idJugador, long idBeneficio);
    }

    // Becas y descuentos de los jugadores (HU-011 / HU-012). Reglas del club:
    //  - Un jugador puede tener varios beneficios en el tiempo, pero nunca dos el mismo día (sin superposición).
    //  - Nadie asigna con fecha pasada: empezar antes de hoy borraría deuda ya vencida (eso es condonar, no becar).
    //  - Lo que ya es historia no se edita (ReglasEdicionBeneficio); editar es solo del SuperAdmin (lo controla la API).
    //  - Un jugador dado de baja no recibe ni edita beneficios (los que tenía se cancelaron con la baja).
    //  - Solo se anula un beneficio cancelado; uno vencido sin cancelar ya dio por saldados sus meses.
    public class BeneficiosService : IBeneficiosService
    {
        private const string MensajeSuperposicion =
            "El jugador ya tiene una bonificación que se superpone con ese período. Ajustá las fechas o cancelá la existente.";

        private readonly DiscountDao _discountDao;
        private readonly IJugadoresDao _jugadoresDao;
        private readonly Func<DateTime> _hoy;

        // reloj: solo para las pruebas; en la aplicación se usa la fecha real.
        public BeneficiosService(DiscountDao discountDao, IJugadoresDao jugadoresDao, Func<DateTime>? reloj = null)
        {
            _discountDao = discountDao;
            _jugadoresDao = jugadoresDao;
            _hoy = reloj ?? (() => RelojNegocio.Hoy);
        }

        public IReadOnlyList<Discount> ObtenerVigentes() => _discountDao.GetActiveDiscounts();

        public IReadOnlyList<Discount> ObtenerTodos() => _discountDao.GetAllDiscounts();

        public IReadOnlyList<string> ObtenerMotivos() => _discountDao.GetDiscountTypes().Select(tipo => tipo.Name).ToList();

        public Discount? ObtenerAsignado(long idJugador) => _discountDao.GetAssignedDiscountByPlayer(idJugador);

        public IReadOnlyList<Discount> ObtenerDelJugador(long idJugador) => _discountDao.GetDiscountsByPlayer(idJugador);

        public ResultadoOperacion<Discount> Asignar(long idJugador, SolicitudBeneficio solicitud)
        {
            var (motivo, rechazo) = ValidarJugadorYMotivo(idJugador, solicitud);
            if (rechazo != null) return rechazo;

            Discount beneficio = Construir(idJugador, solicitud, motivo!);
            if (beneficio.StartDate.Date < _hoy().Date)
            {
                return ResultadoOperacion<Discount>.Falla(TipoResultado.NoProcesable,
                    $"La bonificación no puede empezar antes de hoy ({_hoy():dd/MM/yyyy}).");
            }

            // Se consulta antes de grabar solo para nombrar al beneficio que molesta; la escritura vuelve a
            // controlar la superposición dentro de su transacción (una carrera entre dos pedidos también choca).
            Discount? choque = _discountDao.GetOverlappingDiscount(idJugador, beneficio.StartDate, beneficio.EndDate);
            if (choque != null) return Superposicion(choque);

            Discount? creado = _discountDao.CreateDiscount(beneficio);
            if (creado == null) return ResultadoOperacion<Discount>.Falla(TipoResultado.Conflicto, MensajeSuperposicion);

            // Se devuelve lo grabado (con jugador, categoría y estado resueltos), la misma forma que el GET.
            return ResultadoOperacion<Discount>.Ok(Buscar(idJugador, creado.Id) ?? creado);
        }

        public ResultadoOperacion<Discount> Editar(long idJugador, long idBeneficio, SolicitudBeneficio solicitud)
        {
            var (motivo, rechazo) = ValidarJugadorYMotivo(idJugador, solicitud);
            if (rechazo != null) return rechazo;

            // Sin id explícito se edita el que muestra el formulario.
            Discount? actual = idBeneficio > 0 ? Buscar(idJugador, idBeneficio) : _discountDao.GetAssignedDiscountByPlayer(idJugador);
            if (actual == null)
            {
                return ResultadoOperacion<Discount>.Falla(TipoResultado.NoEncontrado, "El jugador no tiene esa bonificación para editar.");
            }

            Discount beneficio = Construir(idJugador, solicitud, motivo!);
            beneficio.Id = actual.Id;

            var edicion = ReglasEdicionBeneficio.Evaluar(actual, beneficio, _hoy());
            if (edicion != ResultadoEdicionBeneficio.Permitida)
            {
                return ResultadoOperacion<Discount>.Falla(
                    edicion == ResultadoEdicionBeneficio.Cerrada ? TipoResultado.Conflicto : TipoResultado.NoProcesable,
                    ReglasEdicionBeneficio.Mensaje(edicion));
            }

            Discount? choque = _discountDao.GetOverlappingDiscount(idJugador, beneficio.StartDate, beneficio.EndDate, actual.Id);
            if (choque != null) return Superposicion(choque);

            bool? actualizado = _discountDao.UpdateDiscount(beneficio);
            if (actualizado == null) return ResultadoOperacion<Discount>.Falla(TipoResultado.Conflicto, MensajeSuperposicion);
            if (actualizado == false)
            {
                return ResultadoOperacion<Discount>.Falla(TipoResultado.NoEncontrado, "El jugador no tiene esa bonificación para editar.");
            }

            return ResultadoOperacion<Discount>.Ok(Buscar(idJugador, actual.Id) ?? beneficio);
        }

        // Cancelar corta un beneficio antes de tiempo: la fila queda como historia (inactiva) y los meses que ya
        // corrieron lo siguen aplicando. Uno que cumplió su fecha de fin vence solo, no hace falta cancelarlo.
        public ResultadoOperacion<long> Cancelar(long idJugador, long idBeneficio)
        {
            if (idJugador <= 0) return ResultadoOperacion<long>.Falla(TipoResultado.Invalido, "Indicá un jugador válido.");

            long objetivo = idBeneficio;
            if (objetivo <= 0)
            {
                Discount? actual = _discountDao.GetAssignedDiscountByPlayer(idJugador);
                if (actual == null)
                {
                    return ResultadoOperacion<long>.Falla(TipoResultado.NoEncontrado, "El jugador no tiene una bonificación para cancelar.");
                }
                objetivo = actual.Id;
            }

            return _discountDao.DeactivateDiscount(idJugador, objetivo)
                ? ResultadoOperacion<long>.Ok(objetivo)
                : ResultadoOperacion<long>.Falla(TipoResultado.NoEncontrado, "El jugador no tiene esa bonificación para cancelar.");
        }

        // Anular corrige un beneficio asignado por error: deja de contar para todas sus cuotas, como si nunca se
        // hubiera otorgado (lo ya cobrado se mantiene). Solo uno cancelado; uno vigente o programado se cancela
        // primero, y uno vencido sin cancelar es intocable (esos meses el club ya los dio por saldados).
        public ResultadoOperacion<long> Anular(long idJugador, long idBeneficio)
        {
            if (idJugador <= 0) return ResultadoOperacion<long>.Falla(TipoResultado.Invalido, "Indicá un jugador válido.");
            if (idBeneficio <= 0) return ResultadoOperacion<long>.Falla(TipoResultado.Invalido, "Indicá una bonificación válida.");

            Discount? actual = Buscar(idJugador, idBeneficio);
            if (actual == null)
            {
                return ResultadoOperacion<long>.Falla(TipoResultado.NoEncontrado, "El jugador no tiene esa bonificación para anular.");
            }
            if (actual.Status != DiscountStatus.Cancelled)
            {
                return ResultadoOperacion<long>.Falla(TipoResultado.Conflicto, actual.Status switch
                {
                    DiscountStatus.Expired => "Esta bonificación ya cumplió su vigencia completa: no se puede anular.",
                    DiscountStatus.Voided => "Esta bonificación ya está anulada.",
                    _ => "Solo se puede anular una bonificación cancelada: cancelala primero."
                });
            }

            return _discountDao.VoidDiscount(idJugador, idBeneficio)
                ? ResultadoOperacion<long>.Ok(idBeneficio)
                : ResultadoOperacion<long>.Falla(TipoResultado.NoEncontrado, "El jugador no tiene esa bonificación para anular.");
        }

        // Lo que comparten todas las escrituras: el jugador existe, sigue en el club y el motivo es del catálogo.
        private (DiscountType? Motivo, ResultadoOperacion<Discount>? Rechazo) ValidarJugadorYMotivo(long idJugador, SolicitudBeneficio solicitud)
        {
            if (idJugador is <= 0 or > int.MaxValue)
            {
                return (null, ResultadoOperacion<Discount>.Falla(TipoResultado.Invalido, "Indicá un jugador válido."));
            }

            JugadorResumen? jugador = _jugadoresDao.ObtenerJugadorPorId((int)idJugador);
            if (jugador == null)
            {
                return (null, ResultadoOperacion<Discount>.Falla(TipoResultado.Invalido, $"No existe el jugador #{idJugador}."));
            }

            if (jugador.FechaBaja != null)
            {
                return (null, ResultadoOperacion<Discount>.Falla(TipoResultado.NoProcesable,
                    $"El jugador está dado de baja desde el {jugador.FechaBaja:dd/MM/yyyy}: no se le pueden asignar ni editar becas o descuentos."));
            }

            DiscountType? motivo = _discountDao.GetDiscountTypeByName(solicitud.Motivo.Trim());
            if (motivo == null)
            {
                return (null, ResultadoOperacion<Discount>.Falla(TipoResultado.Invalido,
                    $"El motivo de la bonificación debe ser uno de estos: {string.Join(", ", ObtenerMotivos())}."));
            }

            return (motivo, null);
        }

        // Porcentaje y monto fijo son excluyentes: el que no corresponde al tipo de valor se guarda vacío. El estado
        // no se fija acá: lo resuelve la base al leer la fila.
        private static Discount Construir(long idJugador, SolicitudBeneficio solicitud, DiscountType motivo)
        {
            string tipoValor = solicitud.TipoValor.Trim();
            bool esPorcentaje = tipoValor == Discount.PercentageValueType;
            return new Discount
            {
                PlayerId = idJugador,
                TypeId = motivo.Id,
                Type = motivo.Name,
                ValueType = tipoValor,
                Percentage = esPorcentaje ? solicitud.Porcentaje : null,
                FixedAmount = esPorcentaje ? null : solicitud.MontoFijo,
                StartDate = solicitud.Inicio,
                EndDate = solicitud.Fin
            };
        }

        // Un beneficio puntual del jugador. Los cancelados a mano no se alcanzan, igual que en el resto del sistema.
        private Discount? Buscar(long idJugador, long idBeneficio)
            => _discountDao.GetDiscountsByPlayer(idJugador).FirstOrDefault(beneficio => beneficio.Id == idBeneficio);

        // Nombra al beneficio que está en el medio, con su período, para que se sepa qué mover.
        private static ResultadoOperacion<Discount> Superposicion(Discount choque)
            => ResultadoOperacion<Discount>.Falla(TipoResultado.Conflicto,
                $"El jugador ya tiene una bonificación para ese período ({choque.Type}, del {choque.StartDate:dd/MM/yyyy} " +
                $"al {choque.EndDate:dd/MM/yyyy}). Ajustá las fechas o cancelá la existente.");
    }
}
