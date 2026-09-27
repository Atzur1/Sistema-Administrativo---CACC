namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

// Enrollment fee of each player and the payments made against it (HU-033).
//
// It follows the same shape as a monthly fee paid in parts: while something is owed
// there is one pending row (estado = 0) whose monto_final is the balance, and each
// payment is a row of its own (estado = 1) keeping the original amount in monto_base.
// When the balance reaches zero the pending row is deleted, as PagosService does for
// monthly fees, so history, latest payments and debt detail read it without changes.
//
// Methods are virtual so the controller tests can replace the database.
public class EnrollmentDAO
{
    private const string EnrollmentConcept = "Inscripcion";

    private readonly string _connectionString;

    public EnrollmentDAO(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Null when the player was never charged an enrollment fee: women's squad, or
    // players registered before HU-033.
    public virtual Enrollment? GetEnrollmentByPlayerId(long playerId)
    {
        string query = @"
            SELECT PK_id_pago, monto_base, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado
            FROM PAGOS
            WHERE FK_id_jugador = @playerId AND concepto = @concept
            ORDER BY fecha_pago, PK_id_pago;";

        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();

        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@playerId", playerId);
        command.Parameters.AddWithValue("@concept", EnrollmentConcept);

        Enrollment? enrollment = null;
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            enrollment ??= new Enrollment
            {
                PlayerId = playerId,
                Amount = Convert.ToDecimal(reader["monto_base"]),
                DueDate = Convert.ToDateTime(reader["fecha_vencimiento"])
            };

            bool isPaid = Convert.ToBoolean(reader["estado"]);
            if (isPaid)
            {
                enrollment.Payments.Add(new EnrollmentPayment
                {
                    Id = Convert.ToInt64(reader["PK_id_pago"]),
                    Amount = Convert.ToDecimal(reader["monto_final"]),
                    PaymentMethod = reader["metodo_pago"].ToString()?.Trim() ?? "",
                    PaymentDate = Convert.ToDateTime(reader["fecha_pago"])
                });
            }
            else
            {
                enrollment.PendingBalance = Convert.ToDecimal(reader["monto_final"]);
            }
        }

        return enrollment;
    }

    // Returns null when, by the time the pending row is locked, there is no balance
    // left or the amount exceeds it: another payment got in first. The controller
    // already checked it, but only a check under the lock is safe from two payments
    // arriving at the same time.
    public virtual EnrollmentPayment? CreateEnrollmentPayment(long playerId, decimal amount, string paymentMethod, DateTime paymentDate)
    {
        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            string pendingQuery = @"
                SELECT PK_id_pago, monto_base, monto_final, fecha_vencimiento
                FROM PAGOS WITH (UPDLOCK, ROWLOCK)
                WHERE FK_id_jugador = @playerId AND concepto = @concept AND estado = 0;";

            bool found = false;
            long pendingId = 0;
            decimal originalAmount = 0;
            decimal balance = 0;
            DateTime dueDate = DateTime.MinValue;

            // The reader has to be closed before the transaction can be rolled back
            using (SqlCommand command = new SqlCommand(pendingQuery, connection, transaction))
            {
                command.Parameters.AddWithValue("@playerId", playerId);
                command.Parameters.AddWithValue("@concept", EnrollmentConcept);

                using SqlDataReader reader = command.ExecuteReader();
                if (reader.Read())
                {
                    found = true;
                    pendingId = Convert.ToInt64(reader["PK_id_pago"]);
                    originalAmount = Convert.ToDecimal(reader["monto_base"]);
                    balance = Convert.ToDecimal(reader["monto_final"]);
                    dueDate = Convert.ToDateTime(reader["fecha_vencimiento"]);
                }
            }

            if (!found || amount > balance)
            {
                transaction.Rollback();
                return null;
            }

            long paymentId = InsertPayment(connection, transaction, playerId, originalAmount, amount, paymentMethod, paymentDate, dueDate);

            decimal remaining = balance - amount;
            if (remaining == 0)
            {
                ExecutePendingUpdate(connection, transaction, "DELETE FROM PAGOS WHERE PK_id_pago = @id AND estado = 0;", pendingId, null);
            }
            else
            {
                ExecutePendingUpdate(connection, transaction, "UPDATE PAGOS SET monto_final = @remaining WHERE PK_id_pago = @id AND estado = 0;", pendingId, remaining);
            }

            transaction.Commit();

            return new EnrollmentPayment
            {
                Id = paymentId,
                Amount = amount,
                PaymentMethod = paymentMethod,
                PaymentDate = paymentDate
            };
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // PK_id_pago has no IDENTITY: the table lock held until commit keeps two payments
    // from taking the same id, the same as PagosDao.InsertarPago.
    private static long InsertPayment(SqlConnection connection, SqlTransaction transaction, long playerId, decimal originalAmount, decimal amount, string paymentMethod, DateTime paymentDate, DateTime dueDate)
    {
        string query = @"
            DECLARE @id INT;
            SELECT @id = ISNULL(MAX(PK_id_pago), 0) + 1 FROM PAGOS WITH (TABLOCKX, HOLDLOCK);

            INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
            VALUES (@id, @playerId, @originalAmount, NULL, @amount, @paymentDate, @paymentMethod, @dueDate, 1, @concept);

            SELECT @id;";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@playerId", playerId);
        command.Parameters.AddWithValue("@originalAmount", originalAmount);
        command.Parameters.AddWithValue("@amount", amount);
        command.Parameters.AddWithValue("@paymentDate", paymentDate.Date);
        command.Parameters.AddWithValue("@paymentMethod", paymentMethod);
        command.Parameters.AddWithValue("@dueDate", dueDate.Date);
        command.Parameters.AddWithValue("@concept", EnrollmentConcept);

        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static void ExecutePendingUpdate(SqlConnection connection, SqlTransaction transaction, string query, long pendingId, decimal? remaining)
    {
        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@id", pendingId);
        if (remaining != null)
        {
            command.Parameters.AddWithValue("@remaining", remaining.Value);
        }

        if (command.ExecuteNonQuery() != 1)
        {
            throw new InvalidOperationException($"The pending enrollment fee {pendingId} changed while it was being paid.");
        }
    }
}
