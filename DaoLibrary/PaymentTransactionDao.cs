namespace DaoLibrary;

using EntityLibrary;
using Microsoft.Data.SqlClient;

public class PaymentTransactionDao : IPaymentTransactionDao
{
    private readonly SqlTransactionRunner _transactionRunner;
    private readonly PagosDao _pagosDao;

    public PaymentTransactionDao(string connectionString)
    {
        _transactionRunner = new SqlTransactionRunner(connectionString);
        _pagosDao = new PagosDao(connectionString);
    }

    // SqlTransactionRunner takes the PAGOS write lock (BloqueoEscrituraPagos) before running the operation.
    public T ExecuteInTransaction<T>(Func<IPaymentTransaction, T> operation)
    {
        return _transactionRunner.EjecutarEnTransaccion((connection, transaction) =>
            operation(new PaymentTransaction(_pagosDao, connection, transaction)));
    }

    private sealed class PaymentTransaction : IPaymentTransaction
    {
        private readonly PagosDao _pagosDao;
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        public PaymentTransaction(PagosDao pagosDao, SqlConnection connection, SqlTransaction transaction)
        {
            _pagosDao = pagosDao;
            _connection = connection;
            _transaction = transaction;
        }

        public bool ExisteJugador(int idJugador)
            => _pagosDao.ExisteJugador(_connection, _transaction, idJugador);

        public Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(int idJugador, int mes, int anio)
            => _pagosDao.ObtenerPagoAbonadoDeJugadorEnPeriodo(_connection, _transaction, idJugador, mes, anio);

        public Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(int idJugador, int mes, int anio)
            => _pagosDao.ObtenerPagoPendienteDeJugadorEnPeriodo(_connection, _transaction, idJugador, mes, anio);

        public DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(int idJugador, DateTime fechaVencimiento)
            => _pagosDao.ObtenerDescuentoAplicableEnPeriodo(_connection, _transaction, idJugador, fechaVencimiento);

        public bool EmitirCuotaDeJugador(int idJugador, int mes, int anio)
            => _pagosDao.EmitirCuotaDeJugador(_connection, _transaction, idJugador, mes, anio);

        public int InsertarPago(Pago pago)
            => _pagosDao.InsertarPago(_connection, _transaction, pago);

        public void ActualizarSaldoPendiente(int idPago, decimal nuevoMonto)
            => _pagosDao.ActualizarSaldoPendiente(_connection, _transaction, idPago, nuevoMonto);

        public void EliminarPago(int idPago)
            => _pagosDao.EliminarPago(_connection, _transaction, idPago);

        public IReadOnlyList<Pago> ObtenerPagosPorId(IEnumerable<int> idsPago)
            => _pagosDao.ObtenerPagosPorId(_connection, _transaction, idsPago);

        public void ActualizarMontoCobroConDescuento(int idPago, int idDescuento, decimal montoFinal)
            => _pagosDao.ActualizarMontoCobroConDescuento(_connection, _transaction, idPago, idDescuento, montoFinal);

        public DateTime MarcarPagosComoAbonados(int idJugador, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago, int idUsuarioRegistro)
            => _pagosDao.MarcarPagosComoAbonados(_connection, _transaction, idJugador, idsPago, fechaPago, metodoPago, idUsuarioRegistro);

        public Pago? ObtenerPagoParaAnular(int idPago)
            => _pagosDao.ObtenerPagoParaAnular(_connection, _transaction, idPago);

        public IReadOnlyList<Pago> ObtenerFilasDeLaMismaDeuda(Pago pago)
            => _pagosDao.ObtenerFilasDeLaMismaDeuda(_connection, _transaction, pago);

        public void RegistrarAnulacionYEliminarAbono(Pago abono, string motivo, int idUsuarioAnulacion)
            => _pagosDao.RegistrarAnulacionYEliminarAbono(_connection, _transaction, abono, motivo, idUsuarioAnulacion);
    }
}
