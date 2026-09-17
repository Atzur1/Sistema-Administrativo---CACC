namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

public class TariffDao : ITariffDao
{
    private readonly string _connectionString;

    private const string TariffBaseQuery = @"
        SELECT
            PK_id_tarifa,
            rama,
            monto,
            fecha_inicio,
            fecha_fin
        FROM TARIFAS";

    public TariffDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    public Tariff? GetCurrentTariffByBranch(string branch, DateTime asOfDate)
    {
        string query = TariffBaseQuery +
            " WHERE rama = @branch" +
            " AND fecha_inicio <= @asOfDate" +
            " AND (fecha_fin IS NULL OR fecha_fin >= @asOfDate);";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@branch", branch);
                command.Parameters.AddWithValue("@asOfDate", asOfDate.Date);

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    return reader.Read() ? MapTariff(reader) : null;
                }
            }
        }
    }

    public List<Tariff> GetTariffHistory()
    {
        string query = TariffBaseQuery + " ORDER BY rama, fecha_inicio DESC;";

        List<Tariff> tariffs = new List<Tariff>();

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tariffs.Add(MapTariff(reader));
                    }
                }
            }
        }

        return tariffs;
    }

    public Tariff ScheduleTariff(Tariff newTariff)
    {
        if (newTariff.Branch != "M" && newTariff.Branch != "F")
        {
            throw new ArgumentException("Branch must be either 'M' or 'F'.", nameof(newTariff));
        }

        if (newTariff.Amount <= 0)
        {
            throw new ArgumentException("Amount must be greater than zero.", nameof(newTariff));
        }

        if (newTariff.ValidFrom.Date < DateTime.Today)
        {
            throw new ArgumentException("A tariff cannot be scheduled to start in the past.", nameof(newTariff));
        }

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    Tariff? current = GetOpenTariff(connection, transaction, newTariff.Branch);

                    if (current != null)
                    {
                        if (newTariff.ValidFrom.Date <= current.ValidFrom.Date)
                        {
                            throw new InvalidOperationException(
                                $"The new tariff must start after the branch's current tariff ({current.ValidFrom:yyyy-MM-dd}).");
                        }

                        CloseTariff(connection, transaction, current.Id, newTariff.ValidFrom.Date.AddDays(-1));
                    }

                    long newId = InsertTariff(connection, transaction, newTariff);

                    transaction.Commit();

                    newTariff.Id = newId;
                    newTariff.ValidTo = null;
                    return newTariff;
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }
    }

    // Only the branch's own open-ended row is read here: never the other
    // branch's, so scheduling one branch's tariff cannot be influenced by
    // (or influence) the other's.
    private Tariff? GetOpenTariff(SqlConnection connection, SqlTransaction transaction, string branch)
    {
        const string query = @"
            SELECT PK_id_tarifa, rama, monto, fecha_inicio, fecha_fin
            FROM TARIFAS
            WHERE rama = @branch AND fecha_fin IS NULL;";

        using (SqlCommand command = new SqlCommand(query, connection, transaction))
        {
            command.Parameters.AddWithValue("@branch", branch);

            using (SqlDataReader reader = command.ExecuteReader())
            {
                return reader.Read() ? MapTariff(reader) : null;
            }
        }
    }

    private void CloseTariff(SqlConnection connection, SqlTransaction transaction, long tariffId, DateTime validTo)
    {
        const string query = @"
            UPDATE TARIFAS
            SET fecha_fin = @validTo
            WHERE PK_id_tarifa = @id;";

        using (SqlCommand command = new SqlCommand(query, connection, transaction))
        {
            command.Parameters.AddWithValue("@validTo", validTo);
            command.Parameters.AddWithValue("@id", tariffId);
            command.ExecuteNonQuery();
        }
    }

    private long InsertTariff(SqlConnection connection, SqlTransaction transaction, Tariff tariff)
    {
        const string query = @"
            INSERT INTO TARIFAS (rama, monto, fecha_inicio, fecha_fin)
            OUTPUT INSERTED.PK_id_tarifa
            VALUES (@branch, @amount, @validFrom, NULL);";

        using (SqlCommand command = new SqlCommand(query, connection, transaction))
        {
            command.Parameters.AddWithValue("@branch", tariff.Branch);
            command.Parameters.AddWithValue("@amount", tariff.Amount);
            command.Parameters.AddWithValue("@validFrom", tariff.ValidFrom.Date);
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    private Tariff MapTariff(SqlDataReader reader)
    {
        return new Tariff
        {
            Id = Convert.ToInt64(reader["PK_id_tarifa"]),
            Branch = reader["rama"].ToString() ?? "",
            Amount = Convert.ToDecimal(reader["monto"]),
            ValidFrom = Convert.ToDateTime(reader["fecha_inicio"]),
            ValidTo = reader["fecha_fin"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_fin"])
        };
    }
}
