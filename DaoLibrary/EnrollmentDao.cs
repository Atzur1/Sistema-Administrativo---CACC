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

        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);

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
    // idUsuarioRegistro: el operador autenticado (JWT), para que el cobro deje constancia de quién lo hizo.
    public virtual EnrollmentPayment? CreateEnrollmentPayment(long playerId, decimal amount, string paymentMethod, DateTime paymentDate, int? idUsuarioRegistro = null)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using SqlTransaction transaction = connection.BeginTransaction();
        BloqueoEscrituraPagos.Tomar(connection, transaction);

        try
        {
            string pendingQuery = @"
                SELECT PK_id_pago, monto_base, monto_final, fecha_vencimiento
                FROM PAGOS WITH (UPDLOCK, HOLDLOCK)
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

            long paymentId = InsertPayment(connection, transaction, playerId, originalAmount, amount, paymentMethod, paymentDate, dueDate, idUsuarioRegistro);

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

    // Creates the enrollment row (estado=0, balance = enrollmentAmount - paymentAmount)
    // and the first payment (estado=1) in one transaction. Used for players who were
    // registered before HU-033 and never had an enrollment row, or whose enrollment
    // was omitted at sign-up. Returns null only if the locked check finds an enrollment
    // created concurrently (extremely unlikely for a first-time insert, but the caller handles it).
    public virtual EnrollmentPayment? CreateEnrollmentAndFirstPayment(
        long playerId, decimal enrollmentAmount, decimal paymentAmount,
        string paymentMethod, DateTime paymentDate, int? idUsuarioRegistro = null)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using SqlTransaction transaction = connection.BeginTransaction();
        BloqueoEscrituraPagos.Tomar(connection, transaction);

        try
        {
            // Guard: if a concurrent request already inserted the enrollment, bail out
            // so the caller can retry via the normal CreateEnrollmentPayment path.
            string existsQuery = @"
                SELECT COUNT(1) FROM PAGOS WITH (UPDLOCK, HOLDLOCK)
                WHERE FK_id_jugador = @playerId AND concepto = @concept;";

            using (SqlCommand existsCmd = new SqlCommand(existsQuery, connection, transaction))
            {
                existsCmd.Parameters.AddWithValue("@playerId", playerId);
                existsCmd.Parameters.AddWithValue("@concept", EnrollmentConcept);
                int count = Convert.ToInt32(existsCmd.ExecuteScalar());
                if (count > 0)
                {
                    transaction.Rollback();
                    return null;
                }
            }

            decimal remaining = enrollmentAmount - paymentAmount;

            // Insert the pending balance row only when the fee is not fully paid
            if (remaining > 0)
            {
                string pendingQuery = @"
                    DECLARE @id INT;
                    SET @id = NEXT VALUE FOR dbo.SEQ_PAGOS;
                    INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
                    VALUES (@id, @playerId, @enrollmentAmount, NULL, @remaining, NULL, NULL, @paymentDate, 0, @concept);";

                using SqlCommand pendingCmd = new SqlCommand(pendingQuery, connection, transaction);
                pendingCmd.Parameters.AddWithValue("@playerId", playerId);
                pendingCmd.Parameters.AddWithValue("@enrollmentAmount", enrollmentAmount);
                pendingCmd.Parameters.AddWithValue("@remaining", remaining);
                pendingCmd.Parameters.AddWithValue("@paymentDate", paymentDate.Date);
                pendingCmd.Parameters.AddWithValue("@concept", EnrollmentConcept);
                pendingCmd.ExecuteNonQuery();
            }

            long paymentId = InsertPayment(connection, transaction, playerId, enrollmentAmount, paymentAmount, paymentMethod, paymentDate, paymentDate, idUsuarioRegistro);

            transaction.Commit();

            return new EnrollmentPayment
            {
                Id = paymentId,
                Amount = paymentAmount,
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

    // The id comes from the dbo.SEQ_PAGOS sequence, the same as PagosDao.InsertarPago.
    // fecha_hora_registro y FK_id_usuario_registro: misma trazabilidad que los cobros de cuotas (quién y cuándo).
    private static long InsertPayment(SqlConnection connection, SqlTransaction transaction, long playerId, decimal originalAmount, decimal amount, string paymentMethod, DateTime paymentDate, DateTime dueDate, int? idUsuarioRegistro)
    {
        string query = $@"
            DECLARE @id INT;
            SET @id = NEXT VALUE FOR dbo.SEQ_PAGOS;

            INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro, FK_id_usuario_registro)
            VALUES (@id, @playerId, @originalAmount, NULL, @amount, @paymentDate, @paymentMethod, @dueDate, 1, @concept, {SqlReloj.Ahora}, @idUsuarioRegistro);

            SELECT @id;";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@playerId", playerId);
        command.Parameters.AddWithValue("@originalAmount", originalAmount);
        command.Parameters.AddWithValue("@amount", amount);
        command.Parameters.AddWithValue("@paymentDate", paymentDate.Date);
        command.Parameters.AddWithValue("@paymentMethod", paymentMethod);
        command.Parameters.AddWithValue("@dueDate", dueDate.Date);
        command.Parameters.AddWithValue("@concept", EnrollmentConcept);
        command.Parameters.Add("@idUsuarioRegistro", System.Data.SqlDbType.Int).Value = (object?)idUsuarioRegistro ?? DBNull.Value;

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
