namespace DaoLibrary;

using System.Data;
using System.Text;
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

    // Player search on the Cuotas panel (HU-023). A term made only of digits (dots,
    // spaces and dashes are ignored, so "47.970.803" works) is looked up as part of
    // the DNI; anything else as part of the last name.
    //
    // The last name is compared accent-insensitively: the database collation
    // (Modern_Spanish_CI_AS) tells "Bazan" from "BAZÁN", and 160 of the 577 real
    // players have an accented last name. Ñ stays a letter of its own, as in Spanish.
    //
    // A LIKE with a leading % cannot seek an index, but JUGADORES is a few hundred
    // rows: the scan takes milliseconds. TOP keeps a one-letter-wide term from
    // sending the whole roster.
    public virtual IReadOnlyList<PlayerSearchResult> SearchPlayersForFeeManagement(string searchTerm, int maxResults)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        return SearchPlayers(connection, null, searchTerm, maxResults);
    }

    // Internal with the connection as a parameter so the integration test can run it
    // inside the transaction it rolls back, as with the insert helpers below.
    internal static IReadOnlyList<PlayerSearchResult> SearchPlayers(SqlConnection connection, SqlTransaction? transaction, string searchTerm, int maxResults)
    {
        string term = searchTerm.Trim();
        string? dniDigits = OnlyDniDigits(term);

        string filter = dniDigits != null
            ? @"p.Dni LIKE @pattern ESCAPE '\'"
            : @"p.apellido COLLATE Modern_Spanish_CI_AI LIKE @pattern ESCAPE '\'";

        // Exact DNI or last names that start with the term go first.
        string query = $@"
            SELECT TOP (@maxResults) j.PK_id_jugador, p.nombre, p.apellido, p.Dni, c.nombre_categoria
            FROM JUGADORES j
            JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
            JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
            WHERE {filter}
            ORDER BY
                CASE
                    WHEN LTRIM(RTRIM(p.Dni)) = @exact THEN 0
                    WHEN p.apellido COLLATE Modern_Spanish_CI_AI LIKE @prefix ESCAPE '\' THEN 1
                    ELSE 2
                END,
                p.apellido, p.nombre;";

        string value = dniDigits ?? term;
        string escaped = EscapeLike(value);

        // VarChar, like the columns: an nvarchar parameter would force a conversion of every row.
        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.Add("@maxResults", SqlDbType.Int).Value = maxResults;
        command.Parameters.Add("@pattern", SqlDbType.VarChar, 110).Value = $"%{escaped}%";
        command.Parameters.Add("@prefix", SqlDbType.VarChar, 110).Value = $"{escaped}%";
        command.Parameters.Add("@exact", SqlDbType.VarChar, 110).Value = value;

        List<PlayerSearchResult> results = new List<PlayerSearchResult>();
        using SqlDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new PlayerSearchResult
            {
                Id = Convert.ToInt64(reader["PK_id_jugador"]),
                FirstName = reader["nombre"].ToString()?.Trim() ?? "",
                LastName = reader["apellido"].ToString()?.Trim() ?? "",
                Dni = reader["Dni"].ToString()?.Trim() ?? "",
                CategoryName = reader["nombre_categoria"].ToString()?.Trim() ?? ""
            });
        }

        return results;
    }

    // The digits of a DNI typed with dots, spaces or dashes, or null when the term
    // has anything else (then it is a last name).
    internal static string? OnlyDniDigits(string term)
    {
        string digits = new string(term.Where(char.IsAsciiDigit).ToArray());
        bool onlySeparators = term.All(c => char.IsAsciiDigit(c) || c == '.' || c == ' ' || c == '-');

        return onlySeparators && digits.Length > 0 ? digits : null;
    }

    // %, _ and [ typed by the user are searched as plain characters, not as wildcards.
    internal static string EscapeLike(string value)
    {
        StringBuilder escaped = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (c is '\\' or '%' or '_' or '[')
            {
                escaped.Append('\\');
            }
            escaped.Append(c);
        }
        return escaped.ToString();
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
        BloqueoEscrituraPagos.Tomar(connection, transaction);

        try
        {
            // ExistsPersonByDni (antes de la transacción) solo da el mensaje rápido; esta verificación con
            // bloqueo de rango es la que impide que dos altas simultáneas del mismo DNI pasen las dos.
            using (SqlCommand duplicate = new SqlCommand(
                "SELECT COUNT(*) FROM PERSONA WITH (UPDLOCK, HOLDLOCK) WHERE LTRIM(RTRIM(Dni)) = @dni;", connection, transaction))
            {
                duplicate.Parameters.AddWithValue("@dni", player.Dni.Trim());
                if (Convert.ToInt32(duplicate.ExecuteScalar()) > 0)
                {
                    throw new DaoLibrary.Exceptions.DniDuplicadoException(player.Dni.Trim());
                }
            }

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

    // The three insert helpers are internal (not private) so the integration test can run them inside a
    // transaction it rolls back.
    //
    // OUTPUT INTO (not a bare OUTPUT): PERSONA has an audit trigger (TR_AUDIT_PERSONA) and SQL Server
    // refuses a bare OUTPUT on a table with enabled triggers (Msg 334). This is also what PagosDao does.
    internal static int InsertPerson(SqlConnection connection, SqlTransaction transaction, Player player)
    {
        string query = @"
            DECLARE @nueva TABLE (id INT);

            INSERT INTO PERSONA (genero, fecha_de_nacimiento, Dni, nombre, apellido)
            OUTPUT INSERTED.PK_id_persona INTO @nueva (id)
            VALUES (@gender, @birthDate, @dni, @firstName, @lastName);

            SELECT id FROM @nueva;";

        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.AddWithValue("@gender", player.Gender);
        command.Parameters.AddWithValue("@birthDate", player.BirthDate.Date);
        command.Parameters.AddWithValue("@dni", player.Dni);
        command.Parameters.AddWithValue("@firstName", player.FirstName);
        command.Parameters.AddWithValue("@lastName", player.LastName);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    // The id comes from the dbo.SEQ_JUGADORES sequence (migration V20261011_04): unique even when two
    // registrations run at once, without locking the table.
    internal static int InsertPlayer(SqlConnection connection, SqlTransaction transaction, int personId, Player player)
    {
        string query = @"
            DECLARE @id INT;
            SET @id = NEXT VALUE FOR dbo.SEQ_JUGADORES;

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
    internal static void InsertPendingCharge(SqlConnection connection, SqlTransaction transaction, int playerId, decimal amount, DateTime dueDate, string concept)
    {
        string query = @"
            DECLARE @id INT;
            SET @id = NEXT VALUE FOR dbo.SEQ_PAGOS;

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
