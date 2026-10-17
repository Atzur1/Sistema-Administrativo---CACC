namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using ServiceLibrary;

// Reglas de Becados y Descuentos que se resuelven antes de tocar la base: desde qué cuota rige una
// bonificación, jugador dado de baja, jugador inexistente y anular algo que no está cancelado.
// Hoy simulado: 11 de octubre de 2026 (la cuota de octubre ya se emitió el día 10; la de noviembre se emite el 10/11).
public class BeneficiosServiceTests
{
    private static readonly DateTime Hoy = new(2026, 10, 11);

    private static SolicitudBeneficio Beca(DateTime inicio, DateTime? fin = null) =>
        new("Becado", Discount.PercentageValueType, 100m, null, inicio, fin ?? inicio.AddMonths(3));

    private static (BeneficiosService servicio, DiscountDaoDePrueba dao) Crear(
        JugadorResumen? jugador, Discount? guardado = null, bool conPagos = false, DateTime? hoy = null)
    {
        var dao = new DiscountDaoDePrueba(guardado) { ConPagosEnLaCuotaEmitida = conPagos };
        return (new BeneficiosService(dao, new JugadoresDePrueba(jugador), () => hoy ?? Hoy), dao);
    }

    // ===== Desde qué cuota rige =====

    [Fact]
    public void AsignarEmpezandoEnUnaCuotaYaEmitidaYPagada_NoSePermite()
    {
        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 }, conPagos: true);

        var resultado = servicio.Asignar(3, Beca(new DateTime(2026, 10, 1)));

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("Noviembre 2026", resultado.Mensaje);
        Assert.Contains("10/11/2026", resultado.Mensaje);
    }

    [Fact]
    public void AsignarEmpezandoEnUnaCuotaVieja_NoSePermite_AunqueNoTengaPagos()
    {
        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 });

        var resultado = servicio.Asignar(3, Beca(new DateTime(2026, 9, 1)));

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("Octubre 2026", resultado.Mensaje);
        Assert.Contains("condonar", resultado.Mensaje);
    }

    [Fact]
    public void LaCuotaDelMesEnCursoYaEmitidaSeBecaSiNadiePagoNada()
    {
        var (servicio, dao) = Crear(new JugadorResumen { IdJugador = 3 });

        var resultado = servicio.Asignar(3, Beca(new DateTime(2026, 10, 1), new DateTime(2026, 12, 31)));

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.Equal(new DateTime(2026, 10, 1), dao.Creado!.StartDate);
        Assert.Equal(new DateTime(2026, 12, 31), dao.Creado.EndDate);
    }

    [Fact]
    public void LaFechaSeLlevaAMesCompleto_AunqueLleguePorLaMitad()
    {
        var (servicio, dao) = Crear(new JugadorResumen { IdJugador = 3 });

        // Un día cualquiera de noviembre y un día cualquiera de diciembre: cuotas de noviembre y diciembre enteras.
        var resultado = servicio.Asignar(3, Beca(new DateTime(2026, 11, 17), new DateTime(2026, 12, 5)));

        Assert.True(resultado.Exito, resultado.Mensaje);
        Assert.Equal(new DateTime(2026, 11, 1), dao.Creado!.StartDate);
        Assert.Equal(new DateTime(2026, 12, 31), dao.Creado.EndDate);
    }

    [Fact]
    public void ConLaCuotaEmitidaYaPagada_LaPrimeraEsLaProximaQueSeEmite()
    {
        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 }, conPagos: true);

        var primero = servicio.ObtenerPrimerPeriodo(3);

        Assert.Equal(new DateTime(2026, 11, 1), primero.Periodo);
        Assert.False(primero.CuotaYaEmitida);
        Assert.Equal(new DateTime(2026, 11, 10), primero.Emision);
    }

    [Fact]
    public void ConLaCuotaEmitidaSinPagos_LaPrimeraEsLaDelMesEnCurso()
    {
        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 });

        var primero = servicio.ObtenerPrimerPeriodo(3);

        Assert.Equal(new DateTime(2026, 10, 1), primero.Periodo);
        Assert.True(primero.CuotaYaEmitida);
    }

    // Antes del día 10 la cuota del mes todavía no se emitió: la última emitida es la del mes anterior, que sigue
    // sin vencer hasta el 10. Se puede becar esa, o la del mes en curso cuando se emita.
    [Fact]
    public void AntesDelDia10_LaUltimaEmitidaEsLaDelMesAnterior()
    {
        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 }, hoy: new DateTime(2026, 10, 2));

        var primero = servicio.ObtenerPrimerPeriodo(3);

        Assert.Equal(new DateTime(2026, 9, 1), primero.Periodo);
        Assert.True(primero.CuotaYaEmitida);
    }

    // ===== Otras reglas =====

    [Fact]
    public void AUnJugadorDadoDeBaja_NoSeLeAsignaNada()
    {
        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3, FechaBaja = new DateTime(2026, 9, 30) });

        var resultado = servicio.Asignar(3, Beca(Hoy));

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("dado de baja", resultado.Mensaje);
    }

    [Fact]
    public void UnJugadorQueNoExiste_EsUnPedidoInvalido()
    {
        var (servicio, _) = Crear(null);

        var resultado = servicio.Asignar(99, Beca(Hoy));

        Assert.Equal(TipoResultado.Invalido, resultado.Tipo);
        Assert.Contains("#99", resultado.Mensaje);
    }

    [Theory]
    [InlineData(DiscountStatus.Active, "cancelala primero")]
    [InlineData(DiscountStatus.Expired, "vigencia completa")]
    [InlineData(DiscountStatus.Voided, "ya está anulada")]
    public void SoloSeAnulaUnBeneficioCancelado(DiscountStatus estado, string motivo)
    {
        var guardado = new Discount { Id = 42, PlayerId = 3, Status = estado };

        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 }, guardado);
        var resultado = servicio.Anular(3, 42);

        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
        Assert.Contains(motivo, resultado.Mensaje);
    }

    [Fact]
    public void UnBeneficioCanceladoSeAnula()
    {
        var guardado = new Discount { Id = 42, PlayerId = 3, Status = DiscountStatus.Cancelled };

        var (servicio, _) = Crear(new JugadorResumen { IdJugador = 3 }, guardado);
        var resultado = servicio.Anular(3, 42);

        Assert.True(resultado.Exito);
        Assert.Equal(42, resultado.Valor);
    }

    private sealed class DiscountDaoDePrueba(Discount? guardado) : DiscountDao("")
    {
        public bool ConPagosEnLaCuotaEmitida { get; set; }
        public Discount? Creado { get; private set; }

        public override DiscountType? GetDiscountTypeByName(string name) => new DiscountType { Id = 1, Name = name };
        public override List<Discount> GetDiscountsByPlayer(long playerId) => guardado == null ? new() : new() { guardado };
        public override bool VoidDiscount(long playerId, long discountId) => guardado?.Id == discountId;
        public override bool HasPaymentsInPeriod(long playerId, DateTime period) => ConPagosEnLaCuotaEmitida;
        public override Discount? GetOverlappingDiscount(long playerId, DateTime startDate, DateTime endDate, long excludeId = 0) => null;

        public override Discount? CreateDiscount(Discount discount)
        {
            discount.Id = 77;
            Creado = discount;
            return discount;
        }
    }

    private sealed class JugadoresDePrueba(JugadorResumen? jugador) : IJugadoresDao
    {
        public ResultadoBajaJugador DarDeBaja(int idJugador, DateTime fechaBaja) => throw new NotSupportedException();
        public bool Reactivar(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<JugadorResumen> ListarJugadores() => new List<JugadorResumen>();
        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => jugador;
    }
}
