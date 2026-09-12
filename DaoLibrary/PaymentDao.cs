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
            ISNULL(pa.estado, 0) AS estado
        FROM PAGOS pa
            INNER JOIN JUGADORES j ON j.PK_id_jugador = pa.FK_id_jugador
            INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria";

    public PaymentDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<Payment> GetLatestPayments(int count)
    {
        string query = PaymentBaseQuery +
            " WHERE ISNULL(pa.estado, 0) = 1" +
            " ORDER BY pa.fecha_pago DESC, pa.PK_id_pago DESC" +
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
            Method = reader["metodo_pago"].ToString() ?? "",
            IsPaid = Convert.ToBoolean(reader["estado"])
        };
    }
}
