namespace DaoLibrary;

using EntityLibrary;
using Microsoft.Data.SqlClient;

public class TariffTransactionDao : ITariffTransactionDao
{
    private readonly SqlTransactionRunner _transactionRunner;
    private readonly ArancelesDao _arancelesDao;
    private readonly PagosDao _pagosDao;

    public TariffTransactionDao(string connectionString)
    {
        _transactionRunner = new SqlTransactionRunner(connectionString);
        _arancelesDao = new ArancelesDao(connectionString);
        _pagosDao = new PagosDao(connectionString);
    }

    // SqlTransactionRunner takes the PAGOS write lock (BloqueoEscrituraPagos) before running the operation,
    // so a tariff change never interleaves with a payment being registered.
    public T ExecuteInTransaction<T>(Func<ITariffTransaction, T> operation)
    {
        return _transactionRunner.EjecutarEnTransaccion((connection, transaction) =>
            operation(new TariffTransaction(_arancelesDao, _pagosDao, connection, transaction)));
    }

    private sealed class TariffTransaction : ITariffTransaction
    {
        private readonly ArancelesDao _arancelesDao;
        private readonly PagosDao _pagosDao;
        private readonly SqlConnection _connection;
        private readonly SqlTransaction _transaction;

        public TariffTransaction(ArancelesDao arancelesDao, PagosDao pagosDao, SqlConnection connection, SqlTransaction transaction)
        {
            _arancelesDao = arancelesDao;
            _pagosDao = pagosDao;
            _connection = connection;
            _transaction = transaction;
        }

        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde)
            => _arancelesDao.ProgramarArancel(_connection, _transaction, genero, idCategoria, monto, vigenteDesde);

        public Arancel? ObtenerPorId(int idArancel)
            => _arancelesDao.ObtenerPorId(_connection, _transaction, idArancel);

        public DateTime? ObtenerVigenteDesdeEnMes(string? genero, int? idCategoria, int anio, int mes)
            => _arancelesDao.ObtenerVigenteDesdeEnMes(_connection, _transaction, genero, idCategoria, anio, mes);

        public DateTime? ObtenerSiguienteVigenteDesde(string? genero, int? idCategoria, DateTime desde)
            => _arancelesDao.ObtenerSiguienteVigenteDesde(_connection, _transaction, genero, idCategoria, desde);

        public void EliminarArancel(int idArancel)
            => _arancelesDao.EliminarArancel(_connection, _transaction, idArancel);

        public void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio)
            => _pagosDao.GenerarCuotasPendientesDelMes(_connection, _transaction, genero, idCategoria, mes, anio);

        public int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _pagosDao.ContarCuotasConPagos(_connection, _transaction, genero, idCategoria, desde, hasta, montoBase);

        public int EliminarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _pagosDao.EliminarCuotasPendientesSinPagos(_connection, _transaction, genero, idCategoria, desde, hasta, montoBase);
    }
}
