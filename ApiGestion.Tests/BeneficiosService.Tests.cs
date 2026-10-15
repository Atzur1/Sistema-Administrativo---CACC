namespace ApiGestion.Tests;

using DaoLibrary;
using EntityLibrary;
using ServiceLibrary;

// Reglas de Becados y Descuentos que se resuelven antes de tocar la base: fecha pasada, jugador dado de baja,
// jugador inexistente y anular algo que no está cancelado. Hoy simulado: 11 de octubre de 2026.
public class BeneficiosServiceTests
{
    private static readonly DateTime Hoy = new(2026, 10, 11);

    private static SolicitudBeneficio Beca(DateTime inicio) =>
        new("Becado", Discount.PercentageValueType, 100m, null, inicio, inicio.AddMonths(3));

    private static BeneficiosService Crear(JugadorResumen? jugador, Discount? guardado = null) =>
        new(new DiscountDaoDePrueba(guardado), new JugadoresDePrueba(jugador), () => Hoy);

    [Fact]
    public void AsignarConFechaPasada_NoSePermite()
    {
        var resultado = Crear(new JugadorResumen { IdJugador = 3 }).Asignar(3, Beca(Hoy.AddDays(-1)));

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("11/10/2026", resultado.Mensaje);
    }

    [Fact]
    public void AUnJugadorDadoDeBaja_NoSeLeAsignaNada()
    {
        var resultado = Crear(new JugadorResumen { IdJugador = 3, FechaBaja = new DateTime(2026, 9, 30) }).Asignar(3, Beca(Hoy));

        Assert.Equal(TipoResultado.NoProcesable, resultado.Tipo);
        Assert.Contains("dado de baja", resultado.Mensaje);
    }

    [Fact]
    public void UnJugadorQueNoExiste_EsUnPedidoInvalido()
    {
        var resultado = Crear(null).Asignar(99, Beca(Hoy));

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

        var resultado = Crear(new JugadorResumen { IdJugador = 3 }, guardado).Anular(3, 42);

        Assert.Equal(TipoResultado.Conflicto, resultado.Tipo);
        Assert.Contains(motivo, resultado.Mensaje);
    }

    [Fact]
    public void UnBeneficioCanceladoSeAnula()
    {
        var guardado = new Discount { Id = 42, PlayerId = 3, Status = DiscountStatus.Cancelled };

        var resultado = Crear(new JugadorResumen { IdJugador = 3 }, guardado).Anular(3, 42);

        Assert.True(resultado.Exito);
        Assert.Equal(42, resultado.Valor);
    }

    private sealed class DiscountDaoDePrueba(Discount? guardado) : DiscountDao("")
    {
        public override DiscountType? GetDiscountTypeByName(string name) => new DiscountType { Id = 1, Name = name };
        public override List<Discount> GetDiscountsByPlayer(long playerId) => guardado == null ? new() : new() { guardado };
        public override bool VoidDiscount(long playerId, long discountId) => guardado?.Id == discountId;
    }

    private sealed class JugadoresDePrueba(JugadorResumen? jugador) : IJugadoresDao
    {
        public ResultadoBajaJugador DarDeBaja(int idJugador, DateTime fechaBaja) => throw new NotSupportedException();
        public bool Reactivar(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<JugadorResumen> ListarJugadores() => new List<JugadorResumen>();
        public JugadorResumen? ObtenerJugadorPorId(int idJugador) => jugador;
    }
}
