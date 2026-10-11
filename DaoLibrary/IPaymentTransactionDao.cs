namespace DaoLibrary;

// Runs several PAGOS operations as one atomic unit: commit when the operation returns, rollback when it
// throws (the exception is rethrown). The service layer orchestrates a payment without touching
// SqlConnection or SqlTransaction, and is tested with an in-memory fake.
public interface IPaymentTransactionDao
{
    T ExecuteInTransaction<T>(Func<IPaymentTransaction, T> operation);
}
