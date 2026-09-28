namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

// Enrollment fee of the men's squad (HU-033). Kept in its own table and not as one
// more gender in ARANCELES, because everything that reads ARANCELES would take it
// for a monthly fee.
//
// Methods are virtual so the controller tests can replace the database.
public class EnrollmentFeeDAO
{
    private readonly string _connectionString;

    // End date and status are resolved here and nowhere else, against the clock of
    // the database server: a fee ends the day before the next one starts.
    private const string SelectWithStatus = @"
        SELECT
            PK_id_arancel_inscripcion,
            monto,
            vigente_desde,
            DATEADD(DAY, -1, LEAD(vigente_desde) OVER (ORDER BY vigente_desde)) AS vigente_hasta,
            CASE
                WHEN vigente_desde > CAST(GETDATE() AS DATE) THEN 0
                WHEN LEAD(vigente_desde) OVER (ORDER BY vigente_desde) IS NULL
                  OR LEAD(vigente_desde) OVER (ORDER BY vigente_desde) > CAST(GETDATE() AS DATE) THEN 1
                ELSE 2
            END AS estado
        FROM ARANCELES_INSCRIPCION";

    public EnrollmentFeeDAO(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Newest first, the same order the fee table on the screen uses
    public virtual List<EnrollmentFee> GetAllEnrollmentFees()
    {
        List<EnrollmentFee> fees = new List<EnrollmentFee>();
        string query = $@"
            SELECT * FROM ({SelectWithStatus}) f
            ORDER BY f.vigente_desde DESC;";

        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();

        using SqlCommand command = new SqlCommand(query, connection);
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            fees.Add(MapEnrollmentFee(reader));
        }

        return fees;
    }

    public virtual EnrollmentFee? GetCurrentEnrollmentFee()
    {
        string query = $@"
            SELECT * FROM ({SelectWithStatus}) f
            WHERE f.estado = 1;";

        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();

        using SqlCommand command = new SqlCommand(query, connection);
        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapEnrollmentFee(reader);
    }

    public virtual EnrollmentFee? GetEnrollmentFeeByStartDate(DateTime startDate)
    {
        string query = $@"
            SELECT * FROM ({SelectWithStatus}) f
            WHERE f.vigente_desde = @startDate;";

        using SqlConnection connection = new SqlConnection(_connectionString);
        connection.Open();

        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@startDate", startDate.Date);

        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapEnrollmentFee(reader);
    }

    // Reads the row back after inserting, so the caller gets the end date and the
    // status resolved by the database and not the ones it guessed.
    public virtual EnrollmentFee CreateEnrollmentFee(EnrollmentFee fee)
    {
        string query = @"
            INSERT INTO ARANCELES_INSCRIPCION (monto, vigente_desde)
            VALUES (@amount, @startDate);";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using SqlCommand command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@amount", fee.Amount);
            command.Parameters.AddWithValue("@startDate", fee.StartDate.Date);
            command.ExecuteNonQuery();
        }

        return GetEnrollmentFeeByStartDate(fee.StartDate)!;
    }

    private static EnrollmentFee MapEnrollmentFee(SqlDataReader reader)
    {
        return new EnrollmentFee
        {
            Id = Convert.ToInt64(reader["PK_id_arancel_inscripcion"]),
            Amount = Convert.ToDecimal(reader["monto"]),
            StartDate = Convert.ToDateTime(reader["vigente_desde"]),
            EndDate = reader["vigente_hasta"] != DBNull.Value ? Convert.ToDateTime(reader["vigente_hasta"]) : null,
            Status = (EnrollmentFeeStatus)Convert.ToInt32(reader["estado"])
        };
    }
}
