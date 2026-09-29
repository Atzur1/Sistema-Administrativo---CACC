namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

// Registration of new players (HU-033). Methods are virtual so the controller tests
// can replace the database.
public class PlayerDAO
{
    private const string MonthlyFeeConcept = "Cuota";
    private const string EnrollmentFeeConcept = "Inscripcion";

    private readonly string _connectionString;

    public PlayerDAO(string connectionString)
    {
        _connectionString = connectionString;
    }

    // PERSONA is shared with staff, guardians and users, so the DNI is checked
    // against every person and not only against players.
    public virtual bool ExistsPersonByDni(string dni)
    {
        string query = "SELECT COUNT(*) FROM PERSONA WHERE LTRIM(RTRIM(Dni)) = @dni;";

        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);

        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@dni", dni.Trim());

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    // Creates the person, the player and the charges of the registration in one
    // transaction: if any insert fails, nothing is left behind.
    //
    // A null amount means that charge is not generated. Both are stored as the raw
    // amount, frozen at the moment of the registration: a later change of the fee
    // does not touch them, and becas are applied when the charge is read, as with
    // every other monthly fee.
    public virtual Player CreatePlayer(Player player, decimal? monthlyFeeAmount, decimal? enrollmentFeeAmount)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using SqlTransaction transaction = connection.BeginTransaction();

        try
        {
            int personId = InsertPerson(connection, transaction, player);
            int playerId = InsertPlayer(connection, transaction, personId, player);

            if (monthlyFeeAmount != null)
            {
                DateTime firstDayOfMonth = new DateTime(player.JoinDate.Year, player.JoinDate.Month, 1);
                InsertPendingCharge(connection, transaction, playerId, monthlyFeeAmount.Value, firstDayOfMonth, MonthlyFeeConcept);
            }

            if (enrollmentFeeAmount != null)
            {
                InsertPendingCharge(connection, transaction, playerId, enrollmentFeeAmount.Value, player.JoinDate.Date, EnrollmentFeeConcept);
            }

            transaction.Commit();

            player.Id = playerId;
            return player;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static int InsertPerson(SqlConnection connection, SqlTransaction transaction, Player player)
    {
        string query = @"
            INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
            OUTPUT INSERTED.PK_id_persona
            VALUES (@gender, @birthDate, @dni, @firstName, @lastName);";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@gender", player.Gender);
        command.Parameters.AddWithValue("@birthDate", player.BirthDate.Date);
        command.Parameters.AddWithValue("@dni", player.Dni);
        command.Parameters.AddWithValue("@firstName", player.FirstName);
        command.Parameters.AddWithValue("@lastName", player.LastName);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    // JUGADORES.PK_id_jugador has no IDENTITY: the table lock held until commit keeps
    // two registrations running at once from taking the same id.
    private static int InsertPlayer(SqlConnection connection, SqlTransaction transaction, int personId, Player player)
    {
        string query = @"
            DECLARE @id INT;
            SELECT @id = ISNULL(MAX(PK_id_jugador), 0) + 1 FROM JUGADORES WITH (TABLOCKX, HOLDLOCK);

            INSERT INTO JUGADORES (PK_id_jugador, FK_id_persona, FK_id_categoria, fecha_alta)
            VALUES (@id, @personId, @categoryId, @joinDate);

            SELECT @id;";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@personId", personId);
        command.Parameters.AddWithValue("@categoryId", player.CategoryId);
        command.Parameters.AddWithValue("@joinDate", player.JoinDate.Date);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    // Same shape as the pending fees PagosDao generates: no payment date or method yet,
    // no benefit linked, base and final amount equal.
    private static void InsertPendingCharge(SqlConnection connection, SqlTransaction transaction, int playerId, decimal amount, DateTime dueDate, string concept)
    {
        string query = @"
            DECLARE @id INT;
            SELECT @id = ISNULL(MAX(PK_id_pago), 0) + 1 FROM PAGOS WITH (TABLOCKX, HOLDLOCK);

            INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto)
            VALUES (@id, @playerId, @amount, NULL, @amount, NULL, NULL, @dueDate, 0, @concept);";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@playerId", playerId);
        command.Parameters.AddWithValue("@amount", amount);
        command.Parameters.AddWithValue("@dueDate", dueDate);
        command.Parameters.AddWithValue("@concept", concept);
        command.ExecuteNonQuery();
    }
}
