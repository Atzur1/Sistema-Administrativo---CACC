namespace DaoLibrary;

// Runs several ARANCELES/PAGOS operations as one atomic unit: commit when the operation returns, rollback
// when it throws (the exception is rethrown). The service layer schedules or cancels a tariff without
// touching SqlConnection or SqlTransaction, and is tested with an in-memory fake.
public interface ITariffTransactionDao
{
    T ExecuteInTransaction<T>(Func<ITariffTransaction, T> operation);
}
