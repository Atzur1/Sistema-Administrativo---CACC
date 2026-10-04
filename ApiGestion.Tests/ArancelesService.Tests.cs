namespace ApiGestion.Tests;

using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using ServiceLibrary;

// Reglas de ArancelesService: el arancel es por género O por categoría, hay uno solo por mes y destino,
// y un arancel se puede cancelar (con sus cuotas pendientes sin pagos) del mes en curso en adelante.
// Hoy simulado: 3 de octubre de 2026.
public class ArancelesServiceTests
{
    private static readonly DateTime Hoy = new(2026, 10, 3);

    private static (ArancelesService service, FakeArancelesDao aranceles, FakeCuotasDao cuotas) Create()
    {
        var aranceles = new FakeArancelesDao();
        var cuotas = new FakeCuotasDao();
        var service = new ArancelesService(aranceles, cuotas, new FakeRunner(), new FakeCategoriasDao(), () => Hoy);
        return (service, aranceles, cuotas);
    }

    private static ProgramarArancelRequest Request(string? genero, int? idCategoria, decimal monto = 50000, DateTime? desde = null) => new()
    {
        Genero = genero,
        IdCategoria = idCategoria,
        Monto = monto,
        VigenteDesde = desde ?? new DateTime(2026, 10, 1)
    };

    // ===== Validaciones (corren antes de tocar la base) =====

    [Fact]
    public void ProgramarArancel_WithGenderAndCategory_IsRefused()
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request("Masculino", 1)));
        Assert.Contains("género o por categoría", ex.Message);
    }

    [Fact]
    public void ProgramarArancel_WithNeitherGenderNorCategory_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request(null, null)));
        Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request("  ", null)));
    }

    [Fact]
    public void ProgramarArancel_WithAnInvalidGender_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request("Otro", null)));
    }

    [Fact]
    public void ProgramarArancel_WithAnUnknownCategory_IsRefused()
    {
        var ex = Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request(null, 999)));
        Assert.Contains("999", ex.Message);
    }

    [Fact]
    public void ProgramarArancel_WithANonPositiveAmount_IsRefused()
    {
        Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request("Masculino", null, 0)));
        Assert.Throws<ArancelInvalidoException>(() => Create().service.ProgramarArancel(Request(null, 1, -5)));
    }

    // ===== Un solo arancel por mes y destino =====

    [Fact]
    public void ProgramarArancel_WhenTheSameDestinationAlreadyHasOneThatMonth_IsRefusedAndChangesNothing()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.ExistenteEnMes = new DateTime(2026, 10, 2);

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.ProgramarArancel(Request("Masculino", null, 11000, new DateTime(2026, 10, 20))));

        Assert.Contains("02/10/2026", ex.Message);
        Assert.Contains("octubre de 2026", ex.Message);
        Assert.Contains("Cancelalo primero", ex.Message);
        Assert.Empty(aranceles.Programados);
        Assert.Empty(cuotas.Llamadas);
    }

    [Fact]
    public void ProgramarArancel_NamesTheCategoryWhenTheDestinationIsACategory()
    {
        var (service, aranceles, _) = Create();
        aranceles.ExistenteEnMes = new DateTime(2026, 10, 3);

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.ProgramarArancel(Request(null, 1)));

        Assert.Contains("La categoría Sub 17", ex.Message);
    }

    // ===== Qué pasa con las cuotas al programar =====

    [Fact]
    public void ProgramarArancel_ForTheCurrentMonth_ReissuesThePendingCuotasWithoutPaymentsAndKeepsThePaidOnes()
    {
        var (service, aranceles, cuotas) = Create();
        cuotas.ConPagos = 2;
        cuotas.Pendientes = 6;

        var resultado = service.ProgramarArancel(Request(null, 1, 100000, new DateTime(2026, 10, 3)));

        Assert.Single(aranceles.Programados);
        Assert.Equal(6, resultado.CuotasReemitidas);
        Assert.Equal(2, resultado.CuotasConPagosConservadas);
        // Primero se cuentan las que tienen pagos, después se borran las pendientes y recién entonces se vuelve a emitir.
        Assert.Equal(new[] { "contar", "eliminar", "generar" }, cuotas.Llamadas.Select(l => l.Operacion));
        Assert.All(cuotas.Llamadas, l => Assert.Equal(1, l.IdCategoria));
        // Alcanza a toda la categoría y a todos los montos: no se filtra por un monto en particular.
        Assert.Null(cuotas.Llamadas[1].MontoBase);
        Assert.Equal(new DateTime(2026, 10, 1), cuotas.Llamadas[1].Desde);
        Assert.Equal(new DateTime(2026, 11, 1), cuotas.Llamadas[1].Hasta);
    }

    [Fact]
    public void ProgramarArancel_ForAFutureMonth_EmitsNothingNow()
    {
        var (service, aranceles, cuotas) = Create();

        var resultado = service.ProgramarArancel(Request("Femenino", null, 90000, new DateTime(2026, 11, 1)));

        Assert.Single(aranceles.Programados);
        Assert.Empty(cuotas.Llamadas); // las cuotas de noviembre las crea el generador mensual cuando ese mes empiece
        Assert.Equal(0, resultado.CuotasReemitidas);
    }

    [Fact]
    public void ProgramarArancel_ForAPastMonth_OnlyEmitsTheMissingCuotas()
    {
        var (service, _, cuotas) = Create();

        service.ProgramarArancel(Request("Masculino", null, 80000, new DateTime(2026, 9, 21)));

        // Un mes cerrado no se reescribe: no se borra nada, solo se completan las cuotas que falten.
        Assert.Equal(new[] { "generar" }, cuotas.Llamadas.Select(l => l.Operacion));
        Assert.Equal(9, cuotas.Llamadas[0].Mes);
    }

    // ===== Cancelar =====

    [Fact]
    public void CancelarArancel_ThatDoesNotExist_IsRefused()
    {
        var (service, _, _) = Create();

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(77));

        Assert.Contains("no existe", ex.Message);
    }

    [Fact]
    public void CancelarArancel_FromAPastMonth_IsRefusedBecauseItIsHistory()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 5, Genero = "Femenino", Monto = 70000, VigenteDesde = new DateTime(2026, 9, 21) });

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(5));

        Assert.Contains("historial", ex.Message);
        Assert.Empty(aranceles.Eliminados);
        Assert.Empty(cuotas.Llamadas);
    }

    [Fact]
    public void CancelarArancel_WhenSomeOfItsCuotasAlreadyHavePayments_IsRefusedAndDeletesNothing()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 3, Genero = "Masculino", Monto = 11000, VigenteDesde = new DateTime(2026, 10, 20) });
        cuotas.ConPagos = 2;

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(3));

        Assert.Contains("2 cuotas", ex.Message);
        Assert.Contains("pagos registrados", ex.Message);
        Assert.Empty(aranceles.Eliminados);
        Assert.DoesNotContain(cuotas.Llamadas, l => l.Operacion == "eliminar");
    }

    [Fact]
    public void CancelarArancel_UsesSingularWhenOnlyOneCuotaHasPayments()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 3, Genero = "Masculino", Monto = 11000, VigenteDesde = new DateTime(2026, 10, 20) });
        cuotas.ConPagos = 1;

        var ex = Assert.Throws<ArancelInvalidoException>(() => service.CancelarArancel(3));

        Assert.Contains("1 cuota emitida con él ya tiene pagos", ex.Message);
    }

    [Fact]
    public void CancelarArancel_RemovesTheFeeAndItsPendingCuotasMatchedByDestinationPeriodAndAmount()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 3, Genero = "Masculino", Monto = 11000, VigenteDesde = new DateTime(2026, 10, 20) });
        cuotas.Pendientes = 5;

        var resultado = service.CancelarArancel(3);

        Assert.Equal(5, resultado.CuotasEliminadas);
        Assert.Equal(new[] { 3 }, aranceles.Eliminados);

        var eliminar = cuotas.Llamadas.Single(l => l.Operacion == "eliminar");
        Assert.Equal("Masculino", eliminar.Genero);
        Assert.Null(eliminar.IdCategoria);
        Assert.Equal(11000, eliminar.MontoBase); // solo las emitidas con este arancel, no las de otro monto
        Assert.Equal(new DateTime(2026, 10, 1), eliminar.Desde);
        Assert.Null(eliminar.Hasta); // nadie lo reemplazó: cubre de octubre en adelante
    }

    [Fact]
    public void CancelarArancel_OnlyCoversUntilTheMonthOfTheNextFeeOfTheSameDestination()
    {
        var (service, aranceles, cuotas) = Create();
        aranceles.Agregar(new Arancel { IdArancel = 4, IdCategoria = 1, Monto = 100000, VigenteDesde = new DateTime(2026, 10, 3) });
        aranceles.Siguiente = new DateTime(2026, 12, 1);

        service.CancelarArancel(4);

        var eliminar = cuotas.Llamadas.Single(l => l.Operacion == "eliminar");
        Assert.Equal(1, eliminar.IdCategoria);
        Assert.Null(eliminar.Genero);
        Assert.Equal(new DateTime(2026, 12, 1), eliminar.Hasta);
    }

    // ===== Fakes =====

    // Ejecuta la operación enseguida: los fakes no usan la conexión ni la transacción.
    private class FakeRunner : ISqlTransactionRunner
    {
        public T EjecutarEnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operacion) => operacion(null!, null!);
    }

    private class FakeCategoriasDao : ICategoriasDao
    {
        public IReadOnlyList<Categoria> ObtenerTodas() => new List<Categoria>
        {
            new() { IdCategoria = 1, Nombre = "Sub 17" },
            new() { IdCategoria = 2, Nombre = "Sub 15" }
        };
    }

    private class FakeArancelesDao : IArancelesDao
    {
        private readonly Dictionary<int, Arancel> _porId = new();

        public DateTime? ExistenteEnMes { get; set; }
        public DateTime? Siguiente { get; set; }
        public List<(string? Genero, int? IdCategoria, decimal Monto, DateTime Desde)> Programados { get; } = new();
        public List<int> Eliminados { get; } = new();

        public void Agregar(Arancel arancel) => _porId[arancel.IdArancel] = arancel;

        public Arancel? ObtenerPorId(SqlConnection conexion, SqlTransaction transaccion, int idArancel)
            => _porId.TryGetValue(idArancel, out var arancel) ? arancel : null;

        public DateTime? ObtenerVigenteDesdeEnMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int anio, int mes)
            => ExistenteEnMes;

        public DateTime? ObtenerSiguienteVigenteDesde(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde)
            => Siguiente;

        public void EliminarArancel(SqlConnection conexion, SqlTransaction transaccion, int idArancel) => Eliminados.Add(idArancel);

        public void ProgramarArancel(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
            => Programados.Add((genero, idCategoria, monto, vigenteDesde));

        public decimal? ObtenerMontoVigente(string genero, DateTime fecha) => throw new NotSupportedException();
        public decimal? ObtenerMontoVigente(string genero, int idCategoria, DateTime fecha) => throw new NotSupportedException();
        public IReadOnlyList<ArancelHistorialItem> ObtenerHistorial() => throw new NotSupportedException();
        public ArancelResumen ObtenerResumen() => throw new NotSupportedException();
        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde) => throw new NotSupportedException();
    }

    private class FakeCuotasDao : ICuotasPorArancelDao
    {
        public record Llamada(string Operacion, string? Genero, int? IdCategoria, DateTime Desde, DateTime? Hasta, decimal? MontoBase, int Mes);

        public int ConPagos { get; set; }
        public int Pendientes { get; set; }
        public List<Llamada> Llamadas { get; } = new();

        public int ContarCuotasConPagos(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            Llamadas.Add(new Llamada("contar", genero, idCategoria, desde, hasta, montoBase, 0));
            return ConPagos;
        }

        public int EliminarCuotasPendientesSinPagos(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            Llamadas.Add(new Llamada("eliminar", genero, idCategoria, desde, hasta, montoBase, 0));
            return Pendientes;
        }

        public void GenerarCuotasPendientesDelMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int mes, int anio)
            => Llamadas.Add(new Llamada("generar", genero, idCategoria, new DateTime(anio, mes, 1), null, null, mes));
    }
}
