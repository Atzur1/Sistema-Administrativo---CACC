namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using System.Data;
using EntityLibrary;

public class DiscountDao
{
    private readonly string _connectionString;

    // Since HU-011 the benefit itself lives in JUGADORES_DESCUENTOS and
    // TIPO_DESCUENTO only holds the reason, so every projection reads the value
    // and the validity from the assignment row.
    private const string SelectColumns = @"
            jd.PK_id_jugador_descuento,
            jd.FK_id_jugador,
            jd.FK_id_descuento,
            ISNULL(p.apellido, '') + ', ' + ISNULL(p.nombre, '') AS jugador,
            ISNULL(c.nombre_categoria, '') AS categoria,
            ISNULL(td.tipo_descuento, '') AS tipo_descuento,
            ISNULL(jd.tipo_valor, '') AS tipo_valor,
            jd.porcentaje,
            jd.monto_fijo,
            jd.fecha_inicio,
            jd.fecha_fin";

    private const string FromJoins = @"
        FROM JUGADORES_DESCUENTOS jd
            INNER JOIN JUGADORES j ON j.PK_id_jugador = jd.FK_id_jugador
            INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
            INNER JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria";

    // The state of a benefit, resolved here and nowhere else (HU-012).
    //
    // It is computed against GETDATE(), the clock of the database server, so it
    // does not depend on the machine the administrator is sitting at. It is not
    // stored either: a benefit stops being valid the day its range ends, with no
    // scheduled job and no flag for anybody to flip.
    //
    // The numbers match EntityLibrary.DiscountStatus. Cancelling comes first:
    // a benefit taken down by hand is cancelled even if its dates still run.
    private const string StatusExpression = @"
        CASE
            WHEN jd.estado_activo = 0 THEN 3
            WHEN CAST(GETDATE() AS DATE) < jd.fecha_inicio THEN 0
            WHEN CAST(GETDATE() AS DATE) > jd.fecha_fin THEN 2
            ELSE 1
        END";

    // A benefit applies today when it was not cancelled and today falls inside
    // its range. Written against the same criteria as StatusExpression.
    private const string InForceFilter = @"
        jd.estado_activo = 1
        AND CAST(GETDATE() AS DATE) BETWEEN jd.fecha_inicio AND jd.fecha_fin";

    public DiscountDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Feeds the badges: only benefits actually in force are labelled
    public List<Discount> GetActiveDiscounts()
    {
        List<Discount> discounts = new List<Discount>();
        string query = $@"
            SELECT {SelectColumns}, {StatusExpression} AS estado
            {FromJoins}
            WHERE {InForceFilter}
            ORDER BY jd.FK_id_jugador;";

        return ReadDiscounts(query, command => { });
    }

    // The benefit of a player that is not cancelled, whatever its dates say.
    //
    // Since HU-012 a player can hold more than one over time, so this answers
    // the one that applies today and, if there is none, the closest one that
    // still means something: a scheduled benefit that has not started, or the
    // last one that expired. That is what the form has to show.
    public Discount? GetAssignedDiscountByPlayer(long playerId)
    {
        string query = $@"
            SELECT TOP 1 {SelectColumns}, {StatusExpression} AS estado
            {FromJoins}
            WHERE jd.estado_activo = 1 AND jd.FK_id_jugador = @playerId
            ORDER BY
                CASE
                    WHEN CAST(GETDATE() AS DATE) BETWEEN jd.fecha_inicio AND jd.fecha_fin THEN 0
                    WHEN jd.fecha_inicio > CAST(GETDATE() AS DATE) THEN 1
                    ELSE 2
                END,
                jd.fecha_inicio;";

        List<Discount> found = ReadDiscounts(query, command =>
            command.Parameters.AddWithValue("@playerId", playerId));

        return found.Count > 0 ? found[0] : null;
    }

    // Every benefit of a player that was not cancelled, oldest range first.
    // The financial card lists them so the administrator sees the history, what
    // runs today and what is already scheduled.
    public List<Discount> GetDiscountsByPlayer(long playerId)
    {
        string query = $@"
            SELECT {SelectColumns}, {StatusExpression} AS estado
            {FromJoins}
            WHERE jd.estado_activo = 1 AND jd.FK_id_jugador = @playerId
            ORDER BY jd.fecha_inicio;";

        return ReadDiscounts(query, command =>
            command.Parameters.AddWithValue("@playerId", playerId));
    }

    // The administration table lists every assignment ever granted, current,
    // scheduled, expired and cancelled alike.
    public List<Discount> GetAllDiscounts()
    {
        string query = $@"
            SELECT {SelectColumns}, {StatusExpression} AS estado
            {FromJoins}
            ORDER BY {StatusExpression}, jd.fecha_inicio DESC;";

        return ReadDiscounts(query, command => { });
    }

    // The catalogue of reasons. The API validates against this instead of a list
    // hardcoded in C#, so adding a reason is a row and not a deployment.
    public List<DiscountType> GetDiscountTypes()
    {
        List<DiscountType> types = new List<DiscountType>();
        string query = "SELECT PK_id_descuento, tipo_descuento FROM TIPO_DESCUENTO ORDER BY PK_id_descuento;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        types.Add(new DiscountType
                        {
                            Id = Convert.ToInt64(reader["PK_id_descuento"]),
                            Name = reader["tipo_descuento"].ToString() ?? ""
                        });
                    }
                }
            }
        }

        return types;
    }

    // Resolves a reason typed by the client into its catalogue id. Returns null
    // when the reason does not exist, which is how an invalid motive is caught.
    public DiscountType? GetDiscountTypeByName(string name)
    {
        DiscountType? foundType = null;
        string query = "SELECT PK_id_descuento, tipo_descuento FROM TIPO_DESCUENTO WHERE tipo_descuento = @name;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@name", name);

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        foundType = new DiscountType
                        {
                            Id = Convert.ToInt64(reader["PK_id_descuento"]),
                            Name = reader["tipo_descuento"].ToString() ?? ""
                        };
                    }
                }
            }
        }

        return foundType;
    }

    // The benefit of the player whose range collides with the one given, if any.
    //
    // This is what replaced the unique index HU-011 had: two ranges overlap when
    // each one starts before the other ends, which is the standard interval
    // test. Used to answer a readable error before writing; the write path calls
    // it again inside its own transaction, holding the range, so two requests
    // arriving together cannot both find it free.
    public Discount? GetOverlappingDiscount(long playerId, DateTime startDate, DateTime endDate, long excludeId = 0)
    {
        string query = $@"
            SELECT TOP 1 {SelectColumns}, {StatusExpression} AS estado
            {FromJoins}
            WHERE jd.estado_activo = 1
              AND jd.FK_id_jugador = @playerId
              AND jd.PK_id_jugador_descuento <> @excludeId
              AND jd.fecha_inicio <= @endDate
              AND @startDate <= jd.fecha_fin
            ORDER BY jd.fecha_inicio;";

        List<Discount> found = ReadDiscounts(query, command =>
        {
            command.Parameters.AddWithValue("@playerId", playerId);
            command.Parameters.AddWithValue("@excludeId", excludeId);
            command.Parameters.AddWithValue("@startDate", startDate);
            command.Parameters.AddWithValue("@endDate", endDate);
        });

        return found.Count > 0 ? found[0] : null;
    }

    // Assigns a benefit, refusing to write one whose range collides with another
    // of the same player.
    //
    // The check runs inside the transaction and under UPDLOCK / HOLDLOCK, which
    // locks the range that was read and not only the rows found. Without that,
    // two simultaneous assignments would both see the slot free and both write.
    // Since the unique index is gone, this is what holds the rule.
    //
    // Returns null when the range collides, and the caller turns that into a
    // 409 naming the benefit in the way.
    public Discount? CreateDiscount(Discount discount)
    {
        string overlapQuery = @"
            SELECT TOP 1 PK_id_jugador_descuento
            FROM JUGADORES_DESCUENTOS WITH (UPDLOCK, HOLDLOCK)
            WHERE estado_activo = 1
              AND FK_id_jugador = @playerId
              AND fecha_inicio <= @endDate
              AND @startDate <= fecha_fin;";

        string insertQuery = @"
            INSERT INTO JUGADORES_DESCUENTOS
                (FK_id_jugador, FK_id_descuento, estado_activo, tipo_valor,
                 porcentaje, monto_fijo, fecha_inicio, fecha_fin)
            VALUES
                (@playerId, @typeId, 1, @valueType,
                 @percentage, @fixedAmount, @startDate, @endDate);

            SELECT CAST(SCOPE_IDENTITY() AS BIGINT) AS nuevo_id;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    using (SqlCommand check = new SqlCommand(overlapQuery, connection, transaction))
                    {
                        check.Parameters.AddWithValue("@playerId", discount.PlayerId);
                        check.Parameters.AddWithValue("@startDate", discount.StartDate);
                        check.Parameters.AddWithValue("@endDate", discount.EndDate);

                        if (check.ExecuteScalar() != null)
                        {
                            transaction.Rollback();
                            return null;
                        }
                    }

                    using (SqlCommand command = new SqlCommand(insertQuery, connection, transaction))
                    {
                        AddBenefitParameters(command, discount);
                        command.Parameters.AddWithValue("@playerId", discount.PlayerId);
                        command.Parameters.AddWithValue("@typeId", discount.TypeId);

                        using (SqlDataReader reader = command.ExecuteReader())
                        {
                            reader.Read();
                            discount.Id = Convert.ToInt64(reader["nuevo_id"]);
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

        return discount;
    }

    // Edits a benefit of a player, with the same overlap guard as the insert but
    // ignoring the row being edited, which obviously collides with itself.
    //
    // Scoped by estado_activo = 1 so a cancelled record can never be revived by
    // an update. Returns false when the row was not found, null when the new
    // range collides with another benefit of the same player.
    public bool? UpdateDiscount(Discount discount)
    {
        string overlapQuery = @"
            SELECT TOP 1 PK_id_jugador_descuento
            FROM JUGADORES_DESCUENTOS WITH (UPDLOCK, HOLDLOCK)
            WHERE estado_activo = 1
              AND FK_id_jugador = @playerId
              AND PK_id_jugador_descuento <> @id
              AND fecha_inicio <= @endDate
              AND @startDate <= fecha_fin;";

        string updateQuery = @"
            UPDATE JUGADORES_DESCUENTOS
            SET FK_id_descuento = @typeId,
                tipo_valor      = @valueType,
                porcentaje      = @percentage,
                monto_fijo      = @fixedAmount,
                fecha_inicio    = @startDate,
                fecha_fin       = @endDate
            WHERE PK_id_jugador_descuento = @id
              AND FK_id_jugador = @playerId
              AND estado_activo = 1;";

        int affectedRows;

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlTransaction transaction = connection.BeginTransaction(IsolationLevel.Serializable))
            {
                try
                {
                    using (SqlCommand check = new SqlCommand(overlapQuery, connection, transaction))
                    {
                        check.Parameters.AddWithValue("@playerId", discount.PlayerId);
                        check.Parameters.AddWithValue("@id", discount.Id);
                        check.Parameters.AddWithValue("@startDate", discount.StartDate);
                        check.Parameters.AddWithValue("@endDate", discount.EndDate);

                        if (check.ExecuteScalar() != null)
                        {
                            transaction.Rollback();
                            return null;
                        }
                    }

                    using (SqlCommand command = new SqlCommand(updateQuery, connection, transaction))
                    {
                        AddBenefitParameters(command, discount);
                        command.Parameters.AddWithValue("@id", discount.Id);
                        command.Parameters.AddWithValue("@playerId", discount.PlayerId);
                        command.Parameters.AddWithValue("@typeId", discount.TypeId);

                        affectedRows = command.ExecuteNonQuery();
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

        return affectedRows > 0;
    }

    // Cancels one benefit of a player. The row is kept and only flipped to
    // inactive: the administration table has to keep showing what was granted
    // and PAGOS may still point at it.
    public bool DeactivateDiscount(long playerId, long discountId)
    {
        string query = @"
            UPDATE JUGADORES_DESCUENTOS
            SET estado_activo = 0
            WHERE FK_id_jugador = @playerId
              AND PK_id_jugador_descuento = @discountId
              AND estado_activo = 1;";

        int affectedRows;

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@playerId", playerId);
                command.Parameters.AddWithValue("@discountId", discountId);
                affectedRows = command.ExecuteNonQuery();
            }
        }

        return affectedRows > 0;
    }

    // Runs a projection that already carries the estado column
    private List<Discount> ReadDiscounts(string query, Action<SqlCommand> addParameters)
    {
        List<Discount> discounts = new List<Discount>();

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                addParameters(command);

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        discounts.Add(MapDiscount(reader));
                    }
                }
            }
        }

        return discounts;
    }

    // The value half of an assignment, shared by insert and update so both write
    // the exclusivity the same way: the side that does not apply goes in as NULL.
    private static void AddBenefitParameters(SqlCommand command, Discount discount)
    {
        command.Parameters.AddWithValue("@valueType", discount.ValueType);
        command.Parameters.AddWithValue("@percentage", (object?)discount.Percentage ?? DBNull.Value);
        command.Parameters.AddWithValue("@fixedAmount", (object?)discount.FixedAmount ?? DBNull.Value);
        command.Parameters.AddWithValue("@startDate", discount.StartDate);
        command.Parameters.AddWithValue("@endDate", discount.EndDate);
    }

    private Discount MapDiscount(SqlDataReader reader)
    {
        return new Discount
        {
            Id = Convert.ToInt64(reader["PK_id_jugador_descuento"]),
            PlayerId = Convert.ToInt64(reader["FK_id_jugador"]),
            TypeId = Convert.ToInt64(reader["FK_id_descuento"]),
            PlayerName = reader["jugador"].ToString() ?? "",
            Category = reader["categoria"].ToString() ?? "",
            Type = reader["tipo_descuento"].ToString() ?? "",
            ValueType = reader["tipo_valor"].ToString() ?? "",
            Percentage = reader["porcentaje"] == DBNull.Value ? null : Convert.ToDecimal(reader["porcentaje"]),
            FixedAmount = reader["monto_fijo"] == DBNull.Value ? null : Convert.ToDecimal(reader["monto_fijo"]),
            StartDate = Convert.ToDateTime(reader["fecha_inicio"]),
            EndDate = Convert.ToDateTime(reader["fecha_fin"]),
            // Setting Status also settles IsActive, so the two cannot disagree
            Status = (DiscountStatus)Convert.ToInt32(reader["estado"])
        };
    }
}
