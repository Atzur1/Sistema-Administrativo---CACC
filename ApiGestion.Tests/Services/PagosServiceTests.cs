using DaoLibrary;
using DaoLibrary.Exceptions;
using EntityLibrary;
using Microsoft.Data.SqlClient;
using Moq;
using ServiceLibrary;

namespace ApiGestion.Tests.Services
{
    // Test double que reproduce el mismo try/commit/catch/rollback que SqlTransactionRunner,
    // pero sin abrir conexión real: permite probar que el servicio deja rollear la transacción
    // (no traga la excepción) sin depender de una base de datos.
    //
    // Nota: esto prueba el CONTRATO entre el servicio y el runner (si algo falla adentro, el
    // runner hace rollback y relanza). No reemplaza una prueba de integración contra SQL Server
    // real que verifique que las filas efectivamente vuelven a su estado anterior tras el
    // rollback físico — eso requiere un motor de base de datos y queda fuera del alcance de un
    // test unitario.
    public class FakeTransactionRunner : ISqlTransactionRunner
    {
        public bool CommitLlamado { get; private set; }
        public bool RollbackLlamado { get; private set; }

        public T EjecutarEnTransaccion<T>(Func<SqlConnection, SqlTransaction, T> operacion)
        {
            try
            {
                var resultado = operacion(null!, null!);
                CommitLlamado = true;
                return resultado;
            }
            catch
            {
                RollbackLlamado = true;
                throw;
            }
        }
    }

    public class PagosServiceTests
    {
        private readonly Mock<IPagosDao> _pagosDaoMock = new();
        private readonly FakeTransactionRunner _transactionRunner = new();

        private PagosService CrearServicio() => new(_pagosDaoMock.Object, _transactionRunner);

        // ---------- RegistrarPago (form "Registrar pago") ----------

        [Fact]
        public void RegistrarPago_JugadorInvalido_LanzaCobroInvalido()
        {
            var service = CrearServicio();
            var request = new RegistrarPagoRequest { IdJugador = 0, Periodo = "Marzo", Monto = 1000m, MetodoPago = "Efectivo" };

            Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(request));
            _pagosDaoMock.Verify(d => d.ObtenerPagoAbonadoDeJugadorEnPeriodo(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()), Times.Never);
        }

        [Fact]
        public void RegistrarPago_PeriodoInvalido_LanzaCobroInvalido()
        {
            var service = CrearServicio();
            var request = new RegistrarPagoRequest { IdJugador = 1, Periodo = "Mesinventado", Monto = 1000m, MetodoPago = "Efectivo" };

            Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(request));
        }

        [Fact]
        public void RegistrarPago_MontoInvalido_LanzaCobroInvalido()
        {
            var service = CrearServicio();
            var request = new RegistrarPagoRequest { IdJugador = 1, Periodo = "Marzo", Monto = 0, MetodoPago = "Efectivo" };

            Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(request));
        }

        [Fact]
        public void RegistrarPago_MetodoInvalido_LanzaCobroInvalido()
        {
            var service = CrearServicio();
            var request = new RegistrarPagoRequest { IdJugador = 1, Periodo = "Marzo", Monto = 1000m, MetodoPago = "Cripto" };

            Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(request));
        }

        [Fact]
        public void RegistrarPago_YaExistePagoDeEsePeriodo_LanzaCobroInvalido_YHaceRollback_SinInsertar()
        {
            _pagosDaoMock
                .Setup(d => d.ObtenerPagoAbonadoDeJugadorEnPeriodo(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), 1, 3, It.IsAny<int>()))
                .Returns(new Pago { IdPago = 55, IdJugador = 1, Estado = true, MontoFinal = 85000m });

            var service = CrearServicio();
            var request = new RegistrarPagoRequest { IdJugador = 1, Periodo = "Marzo", Monto = 85000m, MetodoPago = "Efectivo" };

            var ex = Assert.Throws<CobroInvalidoException>(() => service.RegistrarPago(request));
            Assert.Contains("Marzo", ex.Message);

            _pagosDaoMock.Verify(d => d.InsertarPago(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<Pago>()), Times.Never);
            Assert.True(_transactionRunner.RollbackLlamado);
            Assert.False(_transactionRunner.CommitLlamado);
        }

        [Fact]
        public void RegistrarPago_Exitoso_InsertaPagoAbonadoYCommitea()
        {
            _pagosDaoMock
                .Setup(d => d.ObtenerPagoAbonadoDeJugadorEnPeriodo(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
                .Returns((Pago?)null);

            _pagosDaoMock
                .Setup(d => d.InsertarPago(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<Pago>()))
                .Returns(1259);

            var service = CrearServicio();
            var request = new RegistrarPagoRequest { IdJugador = 7, Periodo = "Julio", Monto = 90000m, MetodoPago = "Transferencia" };

            var resultado = service.RegistrarPago(request);

            Assert.Equal(1259, resultado.IdPago);
            Assert.Equal(7, resultado.IdJugador);
            Assert.Equal(90000m, resultado.Monto);

            _pagosDaoMock.Verify(d => d.InsertarPago(
                It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(),
                It.Is<Pago>(p => p.IdJugador == 7 && p.MontoFinal == 90000m && p.MontoBase == 90000m && p.MetodoPago == "Transferencia" && p.Estado)),
                Times.Once);

            Assert.True(_transactionRunner.CommitLlamado);
            Assert.False(_transactionRunner.RollbackLlamado);
        }

        // ---------- CobrarPagosPendientes (cobro en lote por id) ----------

        [Fact]
        public void CobrarPagosPendientes_SinIds_LanzaCobroInvalido()
        {
            var service = CrearServicio();
            var request = new CobrarPagosPendientesRequest { IdsPago = new List<int>(), MetodoPago = "Efectivo" };

            Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(request));
            _pagosDaoMock.Verify(d => d.ObtenerPagosPorId(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>()), Times.Never);
        }

        [Fact]
        public void CobrarPagosPendientes_PagoInexistente_LanzaCobroInvalido_YHaceRollback()
        {
            _pagosDaoMock
                .Setup(d => d.ObtenerPagosPorId(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Pago>());

            var service = CrearServicio();
            var request = new CobrarPagosPendientesRequest { IdsPago = new List<int> { 999 }, MetodoPago = "Efectivo" };

            Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(request));

            _pagosDaoMock.Verify(d => d.MarcarPagosComoAbonados(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
            Assert.True(_transactionRunner.RollbackLlamado);
        }

        [Fact]
        public void CobrarPagosPendientes_PagoYaAbonado_LanzaCobroInvalido_YHaceRollback()
        {
            _pagosDaoMock
                .Setup(d => d.ObtenerPagosPorId(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Pago> { new() { IdPago = 5, Estado = true, MontoFinal = 1000m } });

            var service = CrearServicio();
            var request = new CobrarPagosPendientesRequest { IdsPago = new List<int> { 5 }, MetodoPago = "Efectivo" };

            var ex = Assert.Throws<CobroInvalidoException>(() => service.CobrarPagosPendientes(request));
            Assert.Contains("ya fueron abonados", ex.Message, StringComparison.OrdinalIgnoreCase);

            _pagosDaoMock.Verify(d => d.MarcarPagosComoAbonados(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<string>()), Times.Never);
            Assert.True(_transactionRunner.RollbackLlamado);
        }

        [Fact]
        public void CobrarPagosPendientes_Exitoso_MarcaAbonadosYCommitea()
        {
            _pagosDaoMock
                .Setup(d => d.ObtenerPagosPorId(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Pago>
                {
                    new() { IdPago = 1, Estado = false, MontoFinal = 85000m },
                    new() { IdPago = 2, Estado = false, MontoFinal = 85000m }
                });

            var service = CrearServicio();
            var request = new CobrarPagosPendientesRequest { IdsPago = new List<int> { 1, 2 }, MetodoPago = "Transferencia" };

            var resultado = service.CobrarPagosPendientes(request);

            Assert.Equal(170000m, resultado.MontoTotal);
            Assert.Equal(new List<int> { 1, 2 }, resultado.PagosAbonados);

            _pagosDaoMock.Verify(d => d.MarcarPagosComoAbonados(
                It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(),
                It.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 1, 2 })), It.IsAny<DateTime>(), "Transferencia"),
                Times.Once);

            Assert.True(_transactionRunner.CommitLlamado);
            Assert.False(_transactionRunner.RollbackLlamado);
        }

        [Fact]
        public void CobrarPagosPendientes_FallaAlMarcar_PropagaExcepcionYHaceRollback_SinCommitear()
        {
            _pagosDaoMock
                .Setup(d => d.ObtenerPagosPorId(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>()))
                .Returns(new List<Pago> { new() { IdPago = 1, Estado = false, MontoFinal = 500m } });

            _pagosDaoMock
                .Setup(d => d.MarcarPagosComoAbonados(It.IsAny<SqlConnection>(), It.IsAny<SqlTransaction>(), It.IsAny<IEnumerable<int>>(), It.IsAny<DateTime>(), It.IsAny<string>()))
                .Throws(new InvalidOperationException("Fallo simulado de BD"));

            var service = CrearServicio();
            var request = new CobrarPagosPendientesRequest { IdsPago = new List<int> { 1 }, MetodoPago = "Efectivo" };

            Assert.ThrowsAny<Exception>(() => service.CobrarPagosPendientes(request));

            Assert.True(_transactionRunner.RollbackLlamado);
            Assert.False(_transactionRunner.CommitLlamado);
        }
    }
}
