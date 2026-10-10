namespace ApiGestion.Tests;

using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using ServiceLibrary;

// HU-025: cobro de una o varias cuotas pendientes/vencidas de un jugador. Estas pruebas fijan
// las reglas de negocio de PagosService.CobrarPagosPendientes con un DAO en memoria. La
// atomicidad real (commit/rollback) y el bloqueo UPDLOCK se validan contra SQL Server en
// QA-HU-025.md; acá se verifica que el servicio nunca llegue a marcar nada cuando una regla
// falla y que cualquier excepción salga de la transacción (lo que dispara el rollback).
public class PagosServiceCobroTests
{
    private const int Jugador = 12;
    private const int Operador = 7;

    private static Pago Cuota(int id, decimal monto, int mes, int idJugador = Jugador, bool pagada = false, string concepto = "Cuota") => new()
    {
        IdPago = id,
        IdJugador = idJugador,
        MontoBase = monto,
        MontoFinal = monto,
        FechaVencimiento = new DateTime(2026, mes, 1),
        Estado = pagada,
        Concepto = concepto
    };

    private static CobrarPagosPendientesRequest Solicitud(params int[] ids) => new()
    {
        IdJugador = Jugador,
        IdsPago = ids.ToList(),
        MetodoPago = "Transferencia",
        IdUsuarioRegistro = Operador
    };

    private static (PagosService service, FakePagosDao dao, FakeTransactionRunner runner) Crear(params Pago[] pagos)
    {
        var dao = new FakePagosDao();
        dao.Pagos.AddRange(pagos);
        var runner = new FakeTransactionRunner();
        return (new PagosService(dao, runner), dao, runner);
    }

    // ---- Validaciones previas (no abren transacción) ----

    [Fact]
    public void Cobro_SinCuotas_SeRechaza()
    {
        var (service, dao, runner) = Crear();

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud()));

        Assert.Contains("al menos una cuota", ex.Message);
        Assert.Equal(0, runner.Aperturas);
        Assert.Empty(dao.Marcados);
    }

    [Fact]
    public void Cobro_ConIdsRepetidos_SeRechazaSinDeduplicarEnSilencio()
    {
        var (service, dao, runner) = Crear(Cuota(10, 85_000m, 3));

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10, 10)));

        Assert.Contains("repetida", ex.Message);
        Assert.Equal(0, runner.Aperturas);
    }

    [Theory]
    [InlineData("Débito")]
    [InlineData("")]
    [InlineData("Cheque")]
    public void Cobro_ConMetodoInvalido_SeRechaza(string metodo)
    {
        var (service, _, runner) = Crear(Cuota(10, 85_000m, 3));
        var solicitud = Solicitud(10);
        solicitud.MetodoPago = metodo;

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(solicitud));

        Assert.Contains("Método de pago inválido", ex.Message);
        Assert.Equal(0, runner.Aperturas);
    }

    [Fact]
    public void Cobro_SinJugador_SeRechaza()
    {
        var (service, _, runner) = Crear(Cuota(10, 85_000m, 3));
        var solicitud = Solicitud(10);
        solicitud.IdJugador = 0;

        Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(solicitud));
        Assert.Equal(0, runner.Aperturas);
    }

    [Fact]
    public void Cobro_SinOperadorIdentificado_SeRechaza()
    {
        var (service, _, runner) = Crear(Cuota(10, 85_000m, 3));
        var solicitud = Solicitud(10);
        solicitud.IdUsuarioRegistro = 0;

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(solicitud));

        Assert.Contains("usuario", ex.Message);
        Assert.Equal(0, runner.Aperturas);
    }

    // ---- Validaciones dentro de la transacción ----

    [Fact]
    public void Cobro_JugadorInexistente_SeRechazaYNoMarcaNada()
    {
        var (service, dao, runner) = Crear(Cuota(10, 85_000m, 3));
        dao.JugadorExiste = false;

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10)));

        Assert.Contains("no existe", ex.Message);
        Assert.Empty(dao.Marcados);
        Assert.Equal(1, runner.Rollbacks);
    }

    [Fact]
    public void Cobro_CuotaInexistente_SeRechaza()
    {
        var (service, dao, _) = Crear(Cuota(10, 85_000m, 3));

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10, 99)));

        Assert.Contains("99", ex.Message);
        Assert.Empty(dao.Marcados);
    }

    [Fact]
    public void Cobro_CuotaDeOtroJugador_SeRechaza()
    {
        var (service, dao, _) = Crear(Cuota(10, 85_000m, 3), Cuota(11, 85_000m, 4, idJugador: 99));

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10, 11)));

        Assert.Contains("no pertenecen", ex.Message);
        Assert.Empty(dao.Marcados);
    }

    // Caso de concurrencia: el administrador A tenía la pantalla vieja; B ya cobró la cuota. La
    // lectura con bloqueo dentro de la transacción la ve pagada y rechaza todo el lote.
    [Fact]
    public void Cobro_CuotaYaPagada_SeRechazaTodoElLote()
    {
        var (service, dao, runner) = Crear(Cuota(10, 85_000m, 3), Cuota(11, 85_000m, 4, pagada: true));

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10, 11)));

        Assert.Contains("ya fueron abonadas", ex.Message);
        Assert.Empty(dao.Marcados);
        Assert.Equal(1, runner.Rollbacks);
        Assert.Equal(0, runner.Commits);
    }

    [Fact]
    public void Cobro_Inscripcion_NoSeCobraPorEsteFlujo()
    {
        var (service, dao, _) = Crear(Cuota(10, 30_000m, 3, concepto: "Inscripcion"));

        var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10)));

        Assert.Contains("no son cuotas", ex.Message);
        Assert.Empty(dao.Marcados);
    }

    [Fact]
    public void Cobro_CuotaCubiertaPorBeneficio_SeRechaza()
    {
        var (service, dao, _) = Crear(Cuota(10, 85_000m, 3));
        dao.Descuento = new DescuentoAplicable { IdJugadorDescuento = 4, TipoValor = "%", Porcentaje = 100 };

        Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(Solicitud(10)));
        Assert.Empty(dao.Marcados);
    }

    // ---- Camino feliz ----

    // Regla financiera: la cuota de enero se emitió por $70.000; aunque hoy el arancel sea
    // otro, se cobra el importe guardado en la cuota. El servicio no consulta ARANCELES.
    [Fact]
    public void Cobro_UsaElMontoHistoricoDeLaCuota()
    {
        var (service, dao, _) = Crear(Cuota(10, 70_000m, 1));

        var resultado = service.CobrarPagosPendientes(Solicitud(10));

        Assert.Equal(70_000m, resultado.MontoTotal);
        Assert.Equal(70_000m, resultado.Cuotas.Single().Monto);
        Assert.Empty(dao.MontosActualizados);
    }

    [Fact]
    public void Cobro_VariasCuotas_LasMarcaJuntasConOperadorMetodoYTotal()
    {
        var (service, dao, runner) = Crear(Cuota(10, 85_000m, 3), Cuota(11, 85_000m, 4));
        dao.FechaHoraRegistro = new DateTime(2026, 9, 30, 18, 45, 12);

        var resultado = service.CobrarPagosPendientes(Solicitud(10, 11));

        var marcado = Assert.Single(dao.Marcados);
        Assert.Equal(new[] { 10, 11 }, marcado.Ids);
        Assert.Equal(Jugador, marcado.IdJugador);
        Assert.Equal(Operador, marcado.IdUsuario);
        Assert.Equal("Transferencia", marcado.Metodo);
        Assert.Equal(DateTime.Today, marcado.FechaPago);

        Assert.Equal(170_000m, resultado.MontoTotal);
        Assert.Equal(Jugador, resultado.IdJugador);
        Assert.Equal("Transferencia", resultado.MetodoPago);
        Assert.Equal(dao.FechaHoraRegistro, resultado.FechaHoraRegistro);
        Assert.Equal(new[] { "Marzo 2026", "Abril 2026" }, resultado.Cuotas.Select(c => c.Periodo));
        Assert.All(resultado.Cuotas, c => Assert.Equal(EstadosCuota.Pagado, c.Estado));
        Assert.Equal(1, runner.Commits);
    }

    [Fact]
    public void Cobro_NormalizaElMetodoAlValorCanonico()
    {
        var (service, dao, _) = Crear(Cuota(10, 85_000m, 3));
        var solicitud = Solicitud(10);
        solicitud.MetodoPago = "  efectivo ";

        var resultado = service.CobrarPagosPendientes(solicitud);

        Assert.Equal("Efectivo", dao.Marcados.Single().Metodo);
        Assert.Equal("Efectivo", resultado.MetodoPago);
    }

    [Fact]
    public void Cobro_ConBeneficioParcial_CobraElSaldoAjustado()
    {
        var (service, dao, _) = Crear(Cuota(10, 80_000m, 3));
        dao.Descuento = new DescuentoAplicable { IdJugadorDescuento = 4, TipoValor = "%", Porcentaje = 25 };

        var resultado = service.CobrarPagosPendientes(Solicitud(10));

        Assert.Equal(60_000m, resultado.MontoTotal);
        Assert.Equal((10, 4, 60_000m), dao.MontosActualizados.Single());
    }

    // Si la actualización falla a mitad de camino (por ejemplo, otra sesión cobró una cuota
    // entre la lectura y el UPDATE y el DAO detecta menos filas afectadas), la excepción sale de
    // la transacción: el runner real hace rollback y ninguna cuota queda cobrada.
    [Fact]
    public void Cobro_SiFallaLaActualizacion_LaExcepcionSaleDeLaTransaccion()
    {
        var (service, dao, runner) = Crear(Cuota(10, 85_000m, 3), Cuota(11, 85_000m, 4));
        dao.ErrorAlMarcar = new InvalidOperationException("Se esperaba actualizar 2 pago(s) y se actualizaron 1.");

        Assert.Throws<InvalidOperationException>(() => service.CobrarPagosPendientes(Solicitud(10, 11)));

        Assert.Equal(1, runner.Rollbacks);
        Assert.Equal(0, runner.Commits);
    }

    // ---- Estado de cuotas de la ficha financiera ----

    private static CuotaMovimiento Movimiento(int id, int mes, bool pagado, decimal montoFinal, decimal? saldo = null,
        string? metodo = null, DateTime? fechaPago = null, decimal montoBase = 85_000m) => new()
    {
        IdPago = id,
        Estado = pagado,
        MontoBase = montoBase,
        MontoFinal = montoFinal,
        SaldoAjustado = saldo ?? montoFinal,
        FechaVencimiento = new DateTime(2026, mes, 1),
        MetodoPago = metodo,
        FechaPago = fechaPago
    };

    [Fact]
    public void EstadoCuotas_PendienteDentroDelMes_EsPendiente()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(new[] { Movimiento(10, 3, false, 85_000m) }, new DateTime(2026, 3, 31));

        var cuota = Assert.Single(cuotas);
        Assert.Equal(EstadosCuota.Pendiente, cuota.Estado);
        Assert.Equal(85_000m, cuota.SaldoPendiente);
        Assert.Equal(10, cuota.IdPago);
    }

    [Fact]
    public void EstadoCuotas_PendienteDespuesDelCierreDelMes_EsVencido()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(new[] { Movimiento(10, 3, false, 85_000m) }, new DateTime(2026, 4, 1));

        Assert.Equal(EstadosCuota.Vencido, Assert.Single(cuotas).Estado);
    }

    [Fact]
    public void EstadoCuotas_CobradaEnUnSoloPago_EsPagado()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(
            new[] { Movimiento(10, 3, true, 85_000m, metodo: "Efectivo", fechaPago: new DateTime(2026, 4, 2)) },
            new DateTime(2026, 9, 30));

        var cuota = Assert.Single(cuotas);
        Assert.Equal(EstadosCuota.Pagado, cuota.Estado);
        Assert.Equal(0m, cuota.SaldoPendiente);
        Assert.Equal(85_000m, cuota.MontoAbonado);
        Assert.Equal("Efectivo", cuota.MetodoPago);
    }

    [Fact]
    public void EstadoCuotas_ConAbonoParcial_SigueCobrableConElSaldoRestante()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(new[]
        {
            Movimiento(20, 5, true, 30_000m, metodo: "Efectivo", fechaPago: new DateTime(2026, 5, 3)),
            Movimiento(10, 5, false, 55_000m)
        }, new DateTime(2026, 5, 10));

        var cuota = Assert.Single(cuotas);
        Assert.Equal(EstadosCuota.Pendiente, cuota.Estado);
        Assert.Equal(10, cuota.IdPago);
        Assert.Equal(55_000m, cuota.SaldoPendiente);
        Assert.Equal(30_000m, cuota.MontoAbonado);
        Assert.Equal(85_000m, cuota.MontoCuota);
    }

    [Fact]
    public void EstadoCuotas_PagadaEnVariosAbonos_InformaElUltimoMetodo()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(new[]
        {
            Movimiento(20, 6, true, 30_000m, metodo: "Efectivo", fechaPago: new DateTime(2026, 6, 3)),
            Movimiento(21, 6, true, 55_000m, metodo: "Transferencia", fechaPago: new DateTime(2026, 6, 20))
        }, new DateTime(2026, 9, 30));

        var cuota = Assert.Single(cuotas);
        Assert.Equal(EstadosCuota.Pagado, cuota.Estado);
        Assert.Equal(85_000m, cuota.MontoAbonado);
        Assert.Equal("Transferencia", cuota.MetodoPago);
        Assert.Equal(21, cuota.IdPago);
    }

    [Fact]
    public void EstadoCuotas_CubiertaPorBeneficio_NoEsCobrable()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(new[] { Movimiento(10, 3, false, 85_000m, saldo: -5_000m) }, new DateTime(2026, 3, 5));

        var cuota = Assert.Single(cuotas);
        Assert.True(cuota.CubiertaPorBeneficio);
        Assert.Equal(0m, cuota.SaldoPendiente);
    }

    [Fact]
    public void EstadoCuotas_SeOrdenanPorPeriodo()
    {
        var cuotas = PagosService.ConstruirEstadoDeCuotas(new[]
        {
            Movimiento(12, 5, false, 85_000m),
            Movimiento(10, 3, true, 85_000m, metodo: "Efectivo", fechaPago: new DateTime(2026, 3, 2)),
            Movimiento(11, 4, false, 85_000m)
        }, new DateTime(2026, 5, 10));

        Assert.Equal(new[] { "Marzo 2026", "Abril 2026", "Mayo 2026" }, cuotas.Select(c => c.Periodo));
    }

    // ---- Dobles en memoria ----

    private sealed class FakeTransactionRunner : ISqlTransactionRunner
    {
        public int Aperturas { get; private set; }
        public int Commits { get; private set; }
        public int Rollbacks { get; private set; }

        public T EjecutarEnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operacion)
        {
            Aperturas++;
            try
            {
                T resultado = operacion(null!, null!);
                Commits++;
                return resultado;
            }
            catch
            {
                Rollbacks++;
                throw;
            }
        }
    }

    private sealed class FakePagosDao : IPagosDao
    {
public bool EmitirCuotaDeJugador(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio) => throw new NotSupportedException();
        public Pago? ObtenerPagoParaAnular(SqlConnection conexion, SqlTransaction transaccion, int idPago) => throw new NotSupportedException();
        public IReadOnlyList<Pago> ObtenerFilasDeLaMismaDeuda(SqlConnection conexion, SqlTransaction transaccion, Pago pago) => throw new NotSupportedException();
        public void RegistrarAnulacionYEliminarAbono(SqlConnection conexion, SqlTransaction transaccion, Pago abono, string motivo, int idUsuarioAnulacion) => throw new NotSupportedException();
        public List<Pago> Pagos { get; } = new();
        public bool JugadorExiste { get; set; } = true;
        public DescuentoAplicable? Descuento { get; set; }
        public DateTime FechaHoraRegistro { get; set; } = new DateTime(2026, 9, 30, 12, 0, 0);
        public Exception? ErrorAlMarcar { get; set; }
        public List<(int IdJugador, int[] Ids, DateTime FechaPago, string Metodo, int IdUsuario)> Marcados { get; } = new();
        public List<(int IdPago, int IdDescuento, decimal Monto)> MontosActualizados { get; } = new();

        public bool ExisteJugador(SqlConnection conexion, SqlTransaction transaccion, int idJugador) => JugadorExiste;

        public IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago)
        {
            var ids = idsPago.ToHashSet();
            return Pagos.Where(p => ids.Contains(p.IdPago)).ToList();
        }

        public DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, DateTime fechaVencimiento) => Descuento;

        public void ActualizarMontoCobroConDescuento(SqlConnection conexion, SqlTransaction transaccion, int idPago, int idDescuento, decimal montoFinal)
            => MontosActualizados.Add((idPago, idDescuento, montoFinal));

        public DateTime MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, int idJugador, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago, int idUsuarioRegistro)
        {
            if (ErrorAlMarcar != null)
            {
                throw ErrorAlMarcar;
            }

            Marcados.Add((idJugador, idsPago.ToArray(), fechaPago, metodoPago, idUsuarioRegistro));
            return FechaHoraRegistro;
        }

        // El cobro en lote no usa el resto del DAO.
        public Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio) => throw new NotSupportedException();
        public Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio) => throw new NotSupportedException();
        public void ActualizarSaldoPendiente(SqlConnection conexion, SqlTransaction transaccion, int idPago, decimal nuevoMonto) => throw new NotSupportedException();
        public void EliminarPago(SqlConnection conexion, SqlTransaction transaccion, int idPago) => throw new NotSupportedException();
        public int InsertarPago(SqlConnection conexion, SqlTransaction transaccion, Pago pago) => throw new NotSupportedException();
        public IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados(int? idCategoria = null) => throw new NotSupportedException();
        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors) => throw new NotSupportedException();
        public IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => throw new NotSupportedException();
        public IReadOnlyList<PagoReciente> ObtenerTodosLosPagos() => throw new NotSupportedException();
        public ResumenPagos ObtenerResumen() => throw new NotSupportedException();
        public ResumenPagosHoy ObtenerResumenHoy() => throw new NotSupportedException();
        public HistorialPagosResultado ObtenerHistorialPagos(int idJugador, int page, int pageSize) => throw new NotSupportedException();
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador) => throw new NotSupportedException();
        public IReadOnlyList<CuotaMovimiento> ObtenerMovimientosCuotas(int idJugador) => throw new NotSupportedException();
        public PlayerStatementAccount? GetPlayerStatementAccount(int playerId) => throw new NotSupportedException();
        public void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio) => throw new NotSupportedException();
        public void GenerarCuotasPendientesDelMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int mes, int anio) => throw new NotSupportedException();
    }
}
