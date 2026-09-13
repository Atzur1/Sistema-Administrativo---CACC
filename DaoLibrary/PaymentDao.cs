namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

public class PaymentDao
{
    private readonly string _connectionString;

    private const string PaymentBaseQuery = @"
        SELECT
            pa.PK_id_pago,
            pa.FK_id_jugador,
            ISNULL(p.apellido, '') + ', ' + ISNULL(p.nombre, '') AS jugador,
            ISNULL(c.nombre_categoria, '') AS categoria,
            ISNULL(pa.monto_base, 0) AS monto_base,
            ISNULL(pa.monto_final, 0) AS monto_final,
            pa.fecha_pago,
            pa.fecha_vencimiento,
            ISNULL(pa.metodo_pago, '') AS metodo_pago,
            ISNULL(pa.estado, 0) AS estado,
            pa.referencia_pago,
            pa.FK_id_usuario_registro,
            pa.fecha_registro,
            pa.periodo
        FROM PAGOS pa
            INNER JOIN JUGADORES j ON j.PK_id_jugador = pa.FK_id_jugador
            INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria";

    public PaymentDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Newest registration first. fecha_registro is ordered before fecha_pago because
    // fecha_pago has no time and historical rows can carry typos (payment 374 is
    // dated 2926), which would push today's payments down. Historical rows have no
    // fecha_registro, so they come after, ordered by payment date.
    public List<Payment> GetLatestPayments(int count)
    {
        string query = PaymentBaseQuery +
            " WHERE ISNULL(pa.estado, 0) = 1" +
            " ORDER BY pa.fecha_registro DESC, pa.fecha_pago DESC, pa.PK_id_pago DESC" +
            " OFFSET 0 ROWS FETCH NEXT @count ROWS ONLY;";

        return ReadPayments(query, command => command.Parameters.AddWithValue("@count", count));
    }

    // A fee counts as pending when it is registered and not settled. Right now the
    // table has no rows in that state: monthly fee generation is not implemented
    // yet, so this returns an empty list until then.
    public List<Payment> GetPendingFees()
    {
        string query = PaymentBaseQuery +
            " WHERE ISNULL(pa.estado, 0) = 0" +
            " ORDER BY pa.fecha_vencimiento, pa.PK_id_pago;";

        return ReadPayments(query, null);
    }

    public List<Payment> GetPaymentsByPlayer(long playerId)
    {
        string query = PaymentBaseQuery +
            " WHERE pa.FK_id_jugador = @playerId" +
            " ORDER BY pa.periodo DESC, pa.fecha_pago DESC, pa.PK_id_pago DESC;";

        return ReadPayments(query, command => command.Parameters.AddWithValue("@playerId", playerId));
    }

    public Payment? GetPaymentByPlayerAndPeriod(long playerId, DateTime period)
    {
        string query = PaymentBaseQuery +
            " WHERE pa.FK_id_jugador = @playerId AND pa.periodo = @period;";

        List<Payment> payments = ReadPayments(query, command =>
        {
            command.Parameters.AddWithValue("@playerId", playerId);
            command.Parameters.AddWithValue("@period", period.Date);
        });

        return payments.FirstOrDefault();
    }

    // A payment is always inserted as a new paid row, never updated, so the
    // immutability trigger on PAGOS does not block it. The same server timestamp
    // feeds fecha_pago and fecha_registro.
    public Payment CreatePayment(Payment payment)
    {
        string query = @"
            DECLARE @now DATETIME2(0) = SYSDATETIME();

            INSERT INTO PAGOS
                (FK_id_jugador, monto_base, monto_final, fecha_pago, metodo_pago,
                 estado, referencia_pago, FK_id_usuario_registro, fecha_registro, periodo)
            VALUES
                (@playerId, @amount, @amount, CAST(@now AS DATE), @method,
                 1, @reference, @userId, @now, @period);

            SELECT PK_id_pago, fecha_pago, fecha_registro
            FROM PAGOS
            WHERE PK_id_pago = SCOPE_IDENTITY();";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    using (SqlCommand command = new SqlCommand(query, connection, transaction))
                    {
                        command.Parameters.AddWithValue("@playerId", payment.PlayerId);
                        command.Parameters.AddWithValue("@amount", payment.FinalAmount);
                        command.Parameters.AddWithValue("@method", payment.Method);
                        command.Parameters.AddWithValue("@reference", (object?)payment.Reference ?? DBNull.Value);
                        command.Parameters.AddWithValue("@userId", (object?)payment.RegisteredByUserId ?? DBNull.Value);
                        command.Parameters.AddWithValue("@period", (object?)payment.Period ?? DBNull.Value);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            reader.Read();
                            payment.Id = Convert.ToInt64(reader["PK_id_pago"]);
                            payment.PaymentDate = Convert.ToDateTime(reader["fecha_pago"]);
                            payment.RegisteredAt = Convert.ToDateTime(reader["fecha_registro"]);
                        }
                    }

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        payment.BaseAmount = payment.FinalAmount;
        payment.IsPaid = true;
        return payment;
    }

    public TreasuryMetrics GetTreasuryMetrics()
    {
        TreasuryMetrics metrics = new TreasuryMetrics();
        string query = @"
            SELECT
                (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS
                    WHERE ISNULL(estado, 0) = 1
                    AND YEAR(fecha_pago) = YEAR(GETDATE())) AS recaudado_anio,
                (SELECT COUNT(*) FROM PAGOS
                    WHERE ISNULL(estado, 0) = 1
                    AND YEAR(fecha_pago) = YEAR(GETDATE())
                    AND MONTH(fecha_pago) = MONTH(GETDATE())) AS pagos_mes,
                (SELECT COUNT(*) FROM PAGOS
                    WHERE ISNULL(estado, 0) = 0) AS pendientes;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        metrics.CollectedThisYear = Convert.ToDecimal(reader["recaudado_anio"]);
                        metrics.PaymentsThisMonth = Convert.ToInt32(reader["pagos_mes"]);
                        metrics.PendingCount = Convert.ToInt32(reader["pendientes"]);
                    }
                }
            }
        }

        return metrics;
    }

    private List<Payment> ReadPayments(string query, Action<SqlCommand>? addParameters)
    {
        List<Payment> payments = new List<Payment>();

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                if (addParameters != null)
                {
                    addParameters(command);
                }

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        payments.Add(MapPayment(reader));
                    }
                }
            }
        }

        return payments;
    }

    private Payment MapPayment(SqlDataReader reader)
    {
        return new Payment
        {
            Id = Convert.ToInt64(reader["PK_id_pago"]),
            PlayerId = Convert.ToInt64(reader["FK_id_jugador"]),
            PlayerName = reader["jugador"].ToString() ?? "",
            Category = reader["categoria"].ToString() ?? "",
            BaseAmount = Convert.ToDecimal(reader["monto_base"]),
            FinalAmount = Convert.ToDecimal(reader["monto_final"]),
            PaymentDate = reader["fecha_pago"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_pago"]),
            DueDate = reader["fecha_vencimiento"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_vencimiento"]),
            Period = reader["periodo"] == DBNull.Value ? null : Convert.ToDateTime(reader["periodo"]),
            Method = reader["metodo_pago"].ToString() ?? "",
            IsPaid = Convert.ToBoolean(reader["estado"]),
            Reference = reader["referencia_pago"] == DBNull.Value ? null : reader["referencia_pago"].ToString(),
            RegisteredByUserId = reader["FK_id_usuario_registro"] == DBNull.Value ? null : Convert.ToInt64(reader["FK_id_usuario_registro"]),
            RegisteredAt = reader["fecha_registro"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_registro"])
        };
    }
}
