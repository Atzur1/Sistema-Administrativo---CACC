namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

// Enrollment fee (HU-033), one history per gender. Kept in its own table and not as one
// more row of ARANCELES, because everything that reads ARANCELES would take it for a
// monthly fee. A gender with no fee in force (or with a fee of 0) is not charged.
//
// Methods are virtual so the controller tests can replace the database.
public class EnrollmentFeeDAO
{
    private readonly string _connectionString;

    // End date and status are resolved here and nowhere else, against the clock of
    // the database server: a fee ends the day before the next one starts.
    internal const string SelectWithStatus = $@"
        SELECT
            PK_id_arancel_inscripcion,
            genero,
            monto,
            vigente_desde,
            DATEADD(DAY, -1, LEAD(vigente_desde) OVER (PARTITION BY genero ORDER BY vigente_desde)) AS vigente_hasta,
            CASE
                WHEN vigente_desde > {SqlReloj.Hoy} THEN 0
                WHEN LEAD(vigente_desde) OVER (PARTITION BY genero ORDER BY vigente_desde) IS NULL
                  OR LEAD(vigente_desde) OVER (PARTITION BY genero ORDER BY vigente_desde) > {SqlReloj.Hoy} THEN 1
                ELSE 2
            END AS estado,
            actor.nombre_usuario AS responsable_nombre,
            actor.apellido_usuario AS responsable_apellido,
            actor.fecha_utc AS registrado_utc
        FROM ARANCELES_INSCRIPCION ai
        OUTER APPLY (
            SELECT TOP (1) ac.nombre_usuario, ac.apellido_usuario, ac.fecha_utc
            FROM dbo.AUDITORIA_CAMBIOS ac
            WHERE ac.entidad = N'ARANCELES_INSCRIPCION'
              AND ac.id_entidad = CONVERT(NVARCHAR(128), ai.PK_id_arancel_inscripcion)
              AND ac.accion = 'INSERT'
            ORDER BY ac.fecha_utc, ac.PK_id_evento
        ) actor";

    public EnrollmentFeeDAO(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Newest first, the same order the fee table on the screen uses (both genders together)
    public virtual List<EnrollmentFee> GetAllEnrollmentFees()
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        return GetAllCore(connection, null);
    }

    internal static List<EnrollmentFee> GetAllCore(SqlConnection connection, SqlTransaction? transaction)
    {
        List<EnrollmentFee> fees = new List<EnrollmentFee>();
        string query = $@"
            SELECT * FROM ({SelectWithStatus}) f
            ORDER BY f.vigente_desde DESC, f.genero;";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            fees.Add(MapEnrollmentFee(reader));
        }

        return fees;
    }

    // The fee in force today for one gender, or null if that gender has none (it is not charged)
    public virtual EnrollmentFee? GetCurrentEnrollmentFee(string gender)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        return GetCurrentCore(connection, null, gender);
    }

    internal static EnrollmentFee? GetCurrentCore(SqlConnection connection, SqlTransaction? transaction, string gender)
    {
        string query = $@"
            SELECT * FROM ({SelectWithStatus}) f
            WHERE f.estado = 1 AND f.genero = @gender;";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@gender", gender);
        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return MapEnrollmentFee(reader);
    }

    public virtual EnrollmentFee? GetEnrollmentFeeByStartDate(string gender, DateTime startDate)
    {
        string query = $@"
            SELECT * FROM ({SelectWithStatus}) f
            WHERE f.genero = @gender AND f.vigente_desde = @startDate;";

        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);

        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@gender", gender);
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
            INSERT INTO ARANCELES_INSCRIPCION (genero, monto, vigente_desde)
            VALUES (@gender, @amount, @startDate);";

        using (SqlConnection connection = SqlConnectionFactory.Open(_connectionString))
        {

            using SqlCommand command = new SqlCommand(query, connection);
            command.Parameters.AddWithValue("@gender", fee.Gender);
            command.Parameters.AddWithValue("@amount", fee.Amount);
            command.Parameters.AddWithValue("@startDate", fee.StartDate.Date);
            command.ExecuteNonQuery();
        }

        return GetEnrollmentFeeByStartDate(fee.Gender, fee.StartDate)!;
    }

    private static EnrollmentFee MapEnrollmentFee(SqlDataReader reader)
    {
        return new EnrollmentFee
        {
            Id = Convert.ToInt64(reader["PK_id_arancel_inscripcion"]),
            Gender = reader["genero"].ToString()!.Trim(),
            Amount = Convert.ToDecimal(reader["monto"]),
            StartDate = Convert.ToDateTime(reader["vigente_desde"]),
            EndDate = reader["vigente_hasta"] != DBNull.Value ? Convert.ToDateTime(reader["vigente_hasta"]) : null,
            Status = (EnrollmentFeeStatus)Convert.ToInt32(reader["estado"]),
            ResponsibleName = reader["responsable_nombre"] == DBNull.Value ? null : reader["responsable_nombre"].ToString()?.Trim(),
            ResponsibleSurname = reader["responsable_apellido"] == DBNull.Value ? null : reader["responsable_apellido"].ToString()?.Trim(),
            RegisteredAtUtc = reader["registrado_utc"] == DBNull.Value ? null : Convert.ToDateTime(reader["registrado_utc"])
        };
    }
}
