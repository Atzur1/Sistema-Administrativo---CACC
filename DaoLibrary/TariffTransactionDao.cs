namespace DaoLibrary;

using EntityLibrary;
using Microsoft.Data.SqlClient;

public class TariffTransactionDao : ITariffTransactionDao
{
    private readonly string _connectionString;
    private readonly SqlTransactionRunner _transactionRunner;
    private readonly ArancelesDao _arancelesDao;
    private readonly PagosDao _pagosDao;

    public TariffTransactionDao(string connectionString)
    {
        _connectionString = connectionString;
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

    public T ExecuteRead<T>(Func<ILecturaImpactoArancel, T> operation)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        return operation(new TariffReads(_arancelesDao, _pagosDao, connection, null));
    }

    // The impact reads (HU-106), with or without the transaction: the simulation and the confirmation share this class.
    private class TariffReads : ILecturaImpactoArancel
    {
        protected readonly ArancelesDao _arancelesDao;
        protected readonly PagosDao _pagosDao;
        protected readonly SqlConnection _connection;
        private readonly SqlTransaction? _readTransaction;

        public TariffReads(ArancelesDao arancelesDao, PagosDao pagosDao, SqlConnection connection, SqlTransaction? transaction)
        {
            _arancelesDao = arancelesDao;
            _pagosDao = pagosDao;
            _connection = connection;
            _readTransaction = transaction;
        }

        public decimal? ObtenerMontoVigenteDelDestino(string? genero, int? idCategoria, DateTime fecha)
            => _arancelesDao.ObtenerMontoVigenteDelDestino(_connection, _readTransaction, genero, idCategoria, fecha);

        public DateTime? ObtenerVigenteDesdeEnMes(string? genero, int? idCategoria, int anio, int mes)
            => _arancelesDao.ObtenerVigenteDesdeEnMes(_connection, _readTransaction, genero, idCategoria, anio, mes);

        public DateTime? ObtenerSiguienteVigenteDesde(string? genero, int? idCategoria, DateTime desde)
            => _arancelesDao.ObtenerSiguienteVigenteDesde(_connection, _readTransaction, genero, idCategoria, desde);

        public ImpactoArancelConsulta ConsultarImpacto(string? genero, int? idCategoria, decimal montoNuevo, DateTime vigenteDesde)
            => _arancelesDao.ConsultarImpacto(_connection, _readTransaction, genero, idCategoria, montoNuevo, vigenteDesde);

        public int ContarCuotasConPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _pagosDao.ContarCuotasConPagos(_connection, _readTransaction, genero, idCategoria, desde, hasta, montoBase);

        public int ContarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta)
            => _pagosDao.ContarCuotasPendientesSinPagos(_connection, _readTransaction, genero, idCategoria, desde, hasta);
    }

    private sealed class TariffTransaction : TariffReads, ITariffTransaction
    {
        private readonly SqlTransaction _transaction;

        public TariffTransaction(ArancelesDao arancelesDao, PagosDao pagosDao, SqlConnection connection, SqlTransaction transaction)
            : base(arancelesDao, pagosDao, connection, transaction)
        {
            _transaction = transaction;
        }

        public void ProgramarArancel(string? genero, int? idCategoria, decimal monto, DateTime vigenteDesde, ImpactoArancel? impacto = null)
            => _arancelesDao.ProgramarArancel(_connection, _transaction, genero, idCategoria, monto, vigenteDesde, impacto);

        public Arancel? ObtenerPorId(int idArancel)
            => _arancelesDao.ObtenerPorId(_connection, _transaction, idArancel);

        public void EliminarArancel(int idArancel)
            => _arancelesDao.EliminarArancel(_connection, _transaction, idArancel);

        public void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio)
            => _pagosDao.GenerarCuotasPendientesDelMes(_connection, _transaction, genero, idCategoria, mes, anio);

        public int EliminarCuotasPendientesSinPagos(string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
            => _pagosDao.EliminarCuotasPendientesSinPagos(_connection, _transaction, genero, idCategoria, desde, hasta, montoBase);
    }
}
