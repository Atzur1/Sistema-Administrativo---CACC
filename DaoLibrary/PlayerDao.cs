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

    // PERSONAS is shared with staff, guardians and users, so the DNI is checked
    // against every person and not only against players.
    public virtual bool ExistsPersonByDni(string dni)
    {
        string query = "SELECT COUNT(*) FROM PERSONAS WHERE LTRIM(RTRIM(Dni)) = @dni;";

        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);

        using SqlCommand command = new SqlCommand(query, connection);
        command.Parameters.AddWithValue("@dni", dni.Trim());

        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    // Player search on the Cuotas panel (HU-023). A term made only of digits (dots,
    // spaces and dashes are ignored, so "47.970.803" works) is looked up as part of
    // the DNI; anything else is split into words and EVERY word must be part of the
    // first name or of the last name, in any order: "santiago", "prueba" and
    // "santiago prueba" all find the same player.
    //
    // Names are compared accent-insensitively: the database collation
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
        string[] words = dniDigits != null ? Array.Empty<string>() : SplitSearchWords(term);

        // A term of only separators ("," or ", ,") has nothing to look for: without this it would
        // build an empty filter and return the whole roster.
        if (dniDigits == null && words.Length == 0)
        {
            return new List<PlayerSearchResult>();
        }

        // Each word must be in the first name OR in the last name; all the words must match (AND).
        string filter = dniDigits != null
            ? @"p.Dni LIKE @pattern ESCAPE '\'"
            : string.Join(" AND ", words.Select((_, i) =>
                $@"(p.apellido COLLATE Modern_Spanish_CI_AI LIKE @word{i} ESCAPE '\' OR p.nombre COLLATE Modern_Spanish_CI_AI LIKE @word{i} ESCAPE '\')"));

        // Exact DNI first, then last names that start with the (first) word, then first names that do.
        string query = $@"
            SELECT TOP (@maxResults) j.PK_id_jugador, p.nombre, p.apellido, p.Dni, c.nombre_categoria, j.fecha_baja
            FROM JUGADORES j
            JOIN PERSONAS p ON j.FK_id_persona = p.PK_id_persona
            JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
            WHERE {filter}
            ORDER BY
                CASE
                    WHEN LTRIM(RTRIM(p.Dni)) = @exact THEN 0
                    WHEN p.apellido COLLATE Modern_Spanish_CI_AI LIKE @prefix ESCAPE '\' THEN 1
                    WHEN p.nombre COLLATE Modern_Spanish_CI_AI LIKE @prefix ESCAPE '\' THEN 2
                    ELSE 3
                END,
                p.apellido, p.nombre;";

        string value = dniDigits ?? words[0];
        string escaped = EscapeLike(value);

        // VarChar, like the columns: an nvarchar parameter would force a conversion of every row.
        using SqlCommand command = new SqlCommand(query, connection, transaction);
        command.Parameters.Add("@maxResults", SqlDbType.Int).Value = maxResults;
        command.Parameters.Add("@pattern", SqlDbType.VarChar, 110).Value = $"%{escaped}%";
        command.Parameters.Add("@prefix", SqlDbType.VarChar, 110).Value = $"{escaped}%";
        command.Parameters.Add("@exact", SqlDbType.VarChar, 110).Value = value;
        for (int i = 0; i < words.Length; i++)
        {
            command.Parameters.Add($"@word{i}", SqlDbType.VarChar, 110).Value = $"%{EscapeLike(words[i])}%";
        }

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
                CategoryName = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                LeaveDate = reader["fecha_baja"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_baja"])
            });
        }

        return results;
    }

    // The words of a name search. Spaces and commas separate them, so "Prueba, Santiago" and
    // "santiago prueba" give the same two words. At most MaxSearchWords: more would only slow the
    // query down, and the controller already caps the length of the whole term.
    internal const int MaxSearchWords = 5;

    internal static string[] SplitSearchWords(string term)
    {
        return term
            .Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Take(MaxSearchWords)
            .ToArray();
    }

    // The digits of a DNI typed with dots, spaces or dashes, or null when the term
    // has anything else (then it is a name).
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
                "SELECT COUNT(*) FROM PERSONAS WITH (UPDLOCK, HOLDLOCK) WHERE LTRIM(RTRIM(Dni)) = @dni;", connection, transaction))
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

    // Datos de un jugador que ya existe (por ejemplo, dado de alta desde el Portal Deportivo) para decidir sus cargos.
    // Null si no existe.
    public virtual JugadorParaCargos? ObtenerParaCargos(int idJugador)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using SqlCommand command = new SqlCommand(@"
            SELECT j.PK_id_jugador, p.genero, j.FK_id_categoria, j.fecha_alta, j.fecha_baja
            FROM JUGADORES j
            INNER JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
            WHERE j.PK_id_jugador = @id;", connection);
        command.Parameters.AddWithValue("@id", idJugador);

        using SqlDataReader reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        return new JugadorParaCargos(
            reader.GetInt32(0),
            reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
            reader.GetInt32(2),
            reader.GetDateTime(3),
            reader.IsDBNull(4) ? null : reader.GetDateTime(4));
    }

    // Graba los cargos de alta que le faltan a un jugador que ya existe, en una transacción. Es idempotente:
    // lo que el jugador ya tiene no se repite (la cuota se identifica por su mes; la inscripción es una sola).
    public virtual CargosGenerados GenerarCargosDeAlta(int idJugador, decimal? montoCuota, DateTime primerDiaDelMes,
        decimal? montoInscripcion, DateTime fechaInscripcion)
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using SqlTransaction transaction = connection.BeginTransaction();
        BloqueoEscrituraPagos.Tomar(connection, transaction);

        try
        {
            CargosGenerados generados = GenerarCargos(connection, transaction, idJugador, montoCuota, primerDiaDelMes, montoInscripcion, fechaInscripcion);
            transaction.Commit();
            return generados;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    // Inscripción que empieza a regir después de que el jugador entró. Se cobra a quien entró MIENTRAS ya estaba programada
    // (entre el día en que se cargó el arancel y el día en que empieza a regir) y todavía no tiene ninguna inscripción:
    // queda como deuda pendiente con vencimiento el día en que empieza a regir. A quien entró antes de que se programara no se
    // le cobra (por ejemplo, las jugadoras que ya estaban cuando la inscripción femenina pasó de $0 a un monto).
    // Es idempotente: quien ya tiene una inscripción (pendiente, pagada o anulada) no recibe otra. Devuelve cuántas generó.
    public virtual int GenerarInscripcionesProgramadas()
    {
        using SqlConnection connection = SqlConnectionFactory.Open(_connectionString);
        using SqlTransaction transaction = connection.BeginTransaction();
        BloqueoEscrituraPagos.Tomar(connection, transaction);

        try
        {
            int generadas = GenerarInscripcionesProgramadas(connection, transaction);
            transaction.Commit();
            return generadas;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    internal static int GenerarInscripcionesProgramadas(SqlConnection connection, SqlTransaction transaction)
    {
        // La inscripción vigente de cada género, con el momento en que se cargó (auditoría). Sin ese dato no se sabe
        // quién entró después de la carga, así que no se genera nada.
        string query = $@"
            SELECT j.PK_id_jugador, f.monto, f.vigente_desde, f.registrado_utc, j.fecha_alta
            FROM ({EnrollmentFeeDAO.SelectWithStatus}) f
            JOIN PERSONAS p ON p.genero = f.genero
            JOIN JUGADORES j ON j.FK_id_persona = p.PK_id_persona
            WHERE f.estado = 1
              AND f.monto > 0
              AND f.registrado_utc IS NOT NULL
              AND j.fecha_baja IS NULL
              AND j.fecha_alta < f.vigente_desde
              AND NOT EXISTS (SELECT 1 FROM PAGOS pg WHERE pg.FK_id_jugador = j.PK_id_jugador AND pg.concepto = @concepto);";

        List<(int IdJugador, decimal Monto, DateTime VigenteDesde)> pendientes = new();
        using (SqlCommand command = new SqlCommand(query, connection, transaction))
        {
            command.Parameters.AddWithValue("@concepto", EnrollmentFeeConcept);
            using SqlDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                DateTime cargada = RelojNegocio.FechaDelClub(reader.GetDateTime(3));
                if (reader.GetDateTime(4).Date >= cargada)
                {
                    pendientes.Add((reader.GetInt32(0), reader.GetDecimal(1), reader.GetDateTime(2)));
                }
            }
        }

        foreach (var (idJugador, monto, vigenteDesde) in pendientes)
        {
            InsertPendingCharge(connection, transaction, idJugador, monto, vigenteDesde.Date, EnrollmentFeeConcept);
        }
        return pendientes.Count;
    }

    internal static CargosGenerados GenerarCargos(SqlConnection connection, SqlTransaction transaction, int idJugador,
        decimal? montoCuota, DateTime primerDiaDelMes, decimal? montoInscripcion, DateTime fechaInscripcion)
    {
        bool inscripcion = false;
        bool cuota = false;

        if (montoInscripcion != null && !ExisteCargo(connection, transaction, idJugador, EnrollmentFeeConcept, null))
        {
            InsertPendingCharge(connection, transaction, idJugador, montoInscripcion.Value, fechaInscripcion.Date, EnrollmentFeeConcept);
            inscripcion = true;
        }

        if (montoCuota != null && !ExisteCargo(connection, transaction, idJugador, MonthlyFeeConcept, primerDiaDelMes.Date))
        {
            InsertPendingCharge(connection, transaction, idJugador, montoCuota.Value, primerDiaDelMes.Date, MonthlyFeeConcept);
            cuota = true;
        }

        return new CargosGenerados(inscripcion, cuota);
    }

    private static bool ExisteCargo(SqlConnection connection, SqlTransaction transaction, int idJugador, string concepto, DateTime? vencimiento)
    {
        using SqlCommand command = new SqlCommand(@"
            SELECT COUNT(*) FROM PAGOS
            WHERE FK_id_jugador = @id AND concepto = @concepto AND (@vencimiento IS NULL OR fecha_vencimiento = @vencimiento);", connection, transaction);
        command.Parameters.AddWithValue("@id", idJugador);
        command.Parameters.AddWithValue("@concepto", concepto);
        command.Parameters.AddWithValue("@vencimiento", (object?)vencimiento ?? DBNull.Value);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    // The three insert helpers are internal (not private) so the integration test can run them inside a
    // transaction it rolls back.
    //
    // OUTPUT INTO (not a bare OUTPUT): PERSONAS has an audit trigger (TR_AUDIT_PERSONA) and SQL Server
    // refuses a bare OUTPUT on a table with enabled triggers (Msg 334). This is also what PagosDao does.
    internal static int InsertPerson(SqlConnection connection, SqlTransaction transaction, Player player)
    {
        string query = @"
            DECLARE @nueva TABLE (id INT);

            INSERT INTO PERSONAS (genero, fecha_de_nacimiento, Dni, nombre, apellido)
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
