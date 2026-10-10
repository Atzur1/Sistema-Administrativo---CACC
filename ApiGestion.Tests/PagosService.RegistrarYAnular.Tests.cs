namespace ApiGestion.Tests;

using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using ServiceLibrary;

// Registro manual de pagos ("Registrar pago") y anulación de pagos cargados por error. Un DAO en
// memoria simula las filas de PAGOS: una cuota pendiente (estado = 0) con su saldo crudo en
// monto_final y cada abono como fila propia (estado = 1) con el monto original en monto_base.
public class PagosServiceRegistrarYAnularTests
{
    private const int Jugador = 12;
    private const int Operador = 7;
    private const decimal Arancel = 10_000m;

    private static RegistrarPagoRequest Pago(decimal monto, string periodo = "Diciembre", int anio = 2026) => new()
    {
        IdJugador = Jugador,
        Periodo = periodo,
        Anio = anio,
        Monto = monto,
        MetodoPago = "Efectivo",
        IdUsuarioRegistro = Operador
    };

    private static (PagosService service, MemoryPagosDao dao) Crear()
    {
        var dao = new MemoryPagosDao();
        return (new PagosService(dao, new InlineTransactionRunner()), dao);
    }

    // ---- Registrar pago ----

    [Fact]
    public void RegistrarPago_SinCuotaEmitida_LaEmiteConElArancelYNoAceptaMasQueEso()
    {
        var (service, dao) = Crear();

        var error = Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(Pago(Arancel + 1)));

        Assert.Contains("supera el saldo", error.Message);
        Assert.DoesNotContain(dao.Filas, f => f.Estado);
    }

    [Fact]
    public void RegistrarPago_UnPesoParaUnMesFuturo_NoDejaElMesSaldado()
    {
        // Antes: sin cuota emitida se insertaba un pago "directo" por cualquier monto y el generador
        // salteaba ese mes para siempre. Ahora el peso es un abono parcial y el resto sigue debiéndose.
        var (service, dao) = Crear();

        service.RegistrarPago(Pago(1));

        var pendiente = Assert.Single(dao.Filas, f => !f.Estado);
        Assert.Equal(Arancel - 1, pendiente.MontoFinal);
        Assert.Equal(Arancel, Assert.Single(dao.Filas, f => f.Estado).MontoBase);
    }

    [Fact]
    public void RegistrarPago_SinArancelOFueraDelClub_SeRechazaSinGrabarNada()
    {
        var (service, dao) = Crear();
        dao.HayArancel = false;

        var error = Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(Pago(5_000)));

        Assert.Contains("no hay un arancel vigente", error.Message);
        Assert.Empty(dao.Filas);
    }

    [Fact]
    public void RegistrarPago_JugadorInexistente_SeRechaza()
    {
        var (service, dao) = Crear();
        dao.JugadorExiste = false;

        Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(Pago(5_000)));
        Assert.Empty(dao.Filas);
    }

    [Fact]
    public void RegistrarPago_SinOperador_SeRechaza()
    {
        var (service, dao) = Crear();
        var request = Pago(5_000);
        request.IdUsuarioRegistro = null;

        Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(request));
        Assert.Empty(dao.Filas);
    }

    [Fact]
    public void RegistrarPago_PagoTotal_BorraLaCuotaPendienteYGuardaElOperador()
    {
        var (service, dao) = Crear();

        service.RegistrarPago(Pago(Arancel));

        var abono = Assert.Single(dao.Filas);
        Assert.True(abono.Estado);
        Assert.Equal(Operador, abono.IdUsuarioRegistro);
    }

    // ---- Anular pago ----

    [Fact]
    public void AnularPago_DeUnaCuotaSaldada_VuelveAAbrirLaDeudaPorElTotal()
    {
        var (service, dao) = Crear();
        var pago = service.RegistrarPago(Pago(Arancel));

        var resultado = service.AnularPago(new AnularPagoRequest { IdPago = pago.IdPago, Motivo = "Se cargó al jugador equivocado", IdUsuarioAnulacion = Operador });

        Assert.Equal(Arancel, resultado.SaldoReabierto);
        var pendiente = Assert.Single(dao.Filas);
        Assert.False(pendiente.Estado);
        Assert.Equal(Arancel, pendiente.MontoFinal);
        Assert.Equal("Se cargó al jugador equivocado", Assert.Single(dao.Anulaciones).Motivo);
    }

    [Fact]
    public void AnularPago_UnAbonoParcial_LeDevuelveSuMontoAlSaldoPendiente()
    {
        var (service, dao) = Crear();
        var primero = service.RegistrarPago(Pago(3_000));
        service.RegistrarPago(Pago(2_000));

        service.AnularPago(new AnularPagoRequest { IdPago = primero.IdPago, Motivo = "Monto mal tipeado", IdUsuarioAnulacion = Operador });

        Assert.Equal(Arancel - 2_000, Assert.Single(dao.Filas, f => !f.Estado).MontoFinal);
        Assert.Single(dao.Filas, f => f.Estado);
    }

    [Fact]
    public void AnularPago_UnoDeVariosAbonosQueSaldaronLaCuota_ReabrePorLoQueFalta()
    {
        var (service, dao) = Crear();
        var primero = service.RegistrarPago(Pago(4_000));
        service.RegistrarPago(Pago(6_000));
        Assert.DoesNotContain(dao.Filas, f => !f.Estado);

        service.AnularPago(new AnularPagoRequest { IdPago = primero.IdPago, Motivo = "Pago duplicado", IdUsuarioAnulacion = Operador });

        Assert.Equal(4_000, Assert.Single(dao.Filas, f => !f.Estado).MontoFinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  no ")]
    public void AnularPago_SinMotivo_SeRechaza(string motivo)
    {
        var (service, dao) = Crear();
        var pago = service.RegistrarPago(Pago(Arancel));

        Assert.Throws<CobroInvalidoException>(() =>
            service.AnularPago(new AnularPagoRequest { IdPago = pago.IdPago, Motivo = motivo, IdUsuarioAnulacion = Operador }));
        Assert.Empty(dao.Anulaciones);
    }

    [Fact]
    public void AnularPago_UnaCuotaPendiente_NoEsUnPagoYSeRechaza()
    {
        var (service, dao) = Crear();
        service.RegistrarPago(Pago(1_000));
        int idPendiente = dao.Filas.Single(f => !f.Estado).IdPago;

        Assert.Throws<CobroInvalidoException>(() =>
            service.AnularPago(new AnularPagoRequest { IdPago = idPendiente, Motivo = "Probando", IdUsuarioAnulacion = Operador }));
    }

    // ---- Dobles en memoria ----

    private sealed class InlineTransactionRunner : ISqlTransactionRunner
    {
        public T EjecutarEnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operacion) => operacion(null!, null!);
    }

    private sealed class MemoryPagosDao : IPagosDao
    {
        public List<Pago> Filas { get; } = new();
        public List<(int IdPago, string Motivo)> Anulaciones { get; } = new();
        public bool JugadorExiste { get; set; } = true;
        public bool HayArancel { get; set; } = true;
        private int _ultimoId;

        private static bool MismoMes(Pago p, int mes, int anio) =>
            p.FechaVencimiento is { } f && f.Month == mes && f.Year == anio && (p.Concepto ?? "Cuota") == "Cuota";

        public bool ExisteJugador(SqlConnection c, SqlTransaction t, int idJugador) => JugadorExiste;

        public Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(SqlConnection c, SqlTransaction t, int idJugador, int mes, int anio) =>
            Filas.FirstOrDefault(p => p.IdJugador == idJugador && !p.Estado && MismoMes(p, mes, anio));

        public Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(SqlConnection c, SqlTransaction t, int idJugador, int mes, int anio) =>
            Filas.FirstOrDefault(p => p.IdJugador == idJugador && p.Estado && MismoMes(p, mes, anio));

        public bool EmitirCuotaDeJugador(SqlConnection c, SqlTransaction t, int idJugador, int mes, int anio)
        {
            if (!JugadorExiste || !HayArancel || Filas.Any(p => p.IdJugador == idJugador && MismoMes(p, mes, anio))) return false;
            Filas.Add(new Pago { IdPago = ++_ultimoId, IdJugador = idJugador, MontoBase = Arancel, MontoFinal = Arancel, FechaVencimiento = new DateTime(anio, mes, 1), Concepto = "Cuota" });
            return true;
        }

        public DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(SqlConnection c, SqlTransaction t, int idJugador, DateTime fechaVencimiento) => null;

        public int InsertarPago(SqlConnection c, SqlTransaction t, Pago pago)
        {
            pago.IdPago = ++_ultimoId;
            pago.Concepto ??= "Cuota";
            Filas.Add(pago);
            return pago.IdPago;
        }

        public void ActualizarSaldoPendiente(SqlConnection c, SqlTransaction t, int idPago, decimal nuevoMonto) =>
            Filas.Single(p => p.IdPago == idPago && !p.Estado).MontoFinal = nuevoMonto;

        public void EliminarPago(SqlConnection c, SqlTransaction t, int idPago) => Filas.RemoveAll(p => p.IdPago == idPago && !p.Estado);

        public Pago? ObtenerPagoParaAnular(SqlConnection c, SqlTransaction t, int idPago) => Filas.FirstOrDefault(p => p.IdPago == idPago);

        public IReadOnlyList<Pago> ObtenerFilasDeLaMismaDeuda(SqlConnection c, SqlTransaction t, Pago pago) =>
            Filas.Where(p => p.IdJugador == pago.IdJugador && MismoMes(p, pago.FechaVencimiento!.Value.Month, pago.FechaVencimiento.Value.Year)).ToList();

        public void RegistrarAnulacionYEliminarAbono(SqlConnection c, SqlTransaction t, Pago abono, string motivo, int idUsuarioAnulacion)
        {
            Anulaciones.Add((abono.IdPago, motivo));
            Filas.RemoveAll(p => p.IdPago == abono.IdPago && p.Estado);
        }

        // No intervienen en estos flujos.
        public IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection c, SqlTransaction t, IEnumerable<int> idsPago) => throw new NotSupportedException();
        public DateTime MarcarPagosComoAbonados(SqlConnection c, SqlTransaction t, int idJugador, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago, int idUsuarioRegistro) => throw new NotSupportedException();
        public void ActualizarMontoCobroConDescuento(SqlConnection c, SqlTransaction t, int idPago, int idDescuento, decimal montoFinal) => throw new NotSupportedException();
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
        public void GenerarCuotasPendientesDelMes(SqlConnection c, SqlTransaction t, string? genero, int? idCategoria, int mes, int anio) => throw new NotSupportedException();
    }
}
