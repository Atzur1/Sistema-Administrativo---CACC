namespace DaoLibrary;

// Runs several ARANCELES/PAGOS operations as one atomic unit: commit when the operation returns, rollback
// when it throws (the exception is rethrown). The service layer schedules or cancels a tariff without
// touching SqlConnection or SqlTransaction, and is tested with an in-memory fake.
public interface ITariffTransactionDao
{
    T ExecuteInTransaction<T>(Func<ITariffTransaction, T> operation);

    // HU-106: the same reads ExecuteInTransaction offers (ILecturaImpactoArancel), on a plain connection: no
    // transaction, no write lock, nothing written. The impact simulation runs here; the confirmation runs the very
    // same reads inside ExecuteInTransaction before saving.
    T ExecuteRead<T>(Func<ILecturaImpactoArancel, T> operation);
}
