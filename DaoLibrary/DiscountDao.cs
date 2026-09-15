namespace DaoLibrary;

using Microsoft.Data.SqlClient;
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

    // A benefit is in force when it has not been cancelled and today falls inside
    // its window. Both dates are optional: a null start means "already running"
    // and a null end means "until somebody cancels it".
    private const string InForceFilter = @"
        jd.estado_activo = 1
        AND (jd.fecha_inicio IS NULL OR CAST(GETDATE() AS DATE) >= jd.fecha_inicio)
        AND (jd.fecha_fin IS NULL OR CAST(GETDATE() AS DATE) <= jd.fecha_fin)";

    public DiscountDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    // Feeds the badges: only benefits actually in force are labelled
    public List<Discount> GetActiveDiscounts()
    {
        List<Discount> discounts = new List<Discount>();
        string query = $"SELECT {SelectColumns} {FromJoins} WHERE {InForceFilter} ORDER BY jd.FK_id_jugador;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Discount discount = MapDiscount(reader);
                        discount.IsActive = true;
                        discounts.Add(discount);
                    }
                }
            }
        }

        return discounts;
    }

    // The open assignment of a player, in force or already expired.
    //
    // "Open" means estado_activo = 1, which is exactly what the unique filtered
    // index guards, so this is the method the single-benefit rule is checked
    // with: a player whose benefit expired last month still holds the slot until
    // somebody cancels it, and the form has to show it instead of silently
    // letting a second one through.
    public Discount? GetAssignedDiscountByPlayer(long playerId)
    {
        Discount? foundDiscount = null;
        string query = $@"
            SELECT {SelectColumns},
                CASE WHEN {InForceFilter} THEN 1 ELSE 0 END AS vigente
            {FromJoins}
            WHERE jd.estado_activo = 1 AND jd.FK_id_jugador = @playerId;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@playerId", playerId);

                using (SqlDataReader reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        foundDiscount = MapDiscount(reader);
                        foundDiscount.IsActive = Convert.ToBoolean(reader["vigente"]);
                    }
                }
            }
        }

        return foundDiscount;
    }

    // The administration table lists every assignment ever granted, current and
    // cancelled alike, so it cannot filter by validity. IsActive is resolved here
    // with the same criteria the badge uses.
    public List<Discount> GetAllDiscounts()
    {
        List<Discount> discounts = new List<Discount>();
        string query = $@"
            SELECT {SelectColumns},
                CASE WHEN {InForceFilter} THEN 1 ELSE 0 END AS vigente
            {FromJoins}
            ORDER BY vigente DESC, jd.fecha_inicio DESC;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        Discount discount = MapDiscount(reader);
                        discount.IsActive = Convert.ToBoolean(reader["vigente"]);
                        discounts.Add(discount);
                    }
                }
            }
        }

        return discounts;
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

    // Assigns a benefit. The unique filtered index is the last line of defence
    // against a second active benefit, so a race that slips past the controller
    // check surfaces here as a duplicate key error instead of corrupt data.
    public Discount CreateDiscount(Discount discount)
    {
        string query = @"
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

            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    using (SqlCommand command = new SqlCommand(query, connection, transaction))
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

    // Edits the open assignment of a player. Scoped by estado_activo = 1 so a
    // cancelled record can never be revived by an update.
    public bool UpdateDiscount(Discount discount)
    {
        string query = @"
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

            using (SqlTransaction transaction = connection.BeginTransaction())
            {
                try
                {
                    using (SqlCommand command = new SqlCommand(query, connection, transaction))
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

    // Cancels the open assignment of a player. The row is kept and only flipped
    // to inactive: the administration table has to keep showing what was granted
    // and PAGOS may still point at it.
    public bool DeactivateDiscount(long playerId)
    {
        string query = @"
            UPDATE JUGADORES_DESCUENTOS
            SET estado_activo = 0
            WHERE FK_id_jugador = @playerId AND estado_activo = 1;";

        int affectedRows;

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@playerId", playerId);
                affectedRows = command.ExecuteNonQuery();
            }
        }

        return affectedRows > 0;
    }

    // The value half of an assignment, shared by insert and update so both write
    // the exclusivity the same way: the side that does not apply goes in as NULL.
    private static void AddBenefitParameters(SqlCommand command, Discount discount)
    {
        command.Parameters.AddWithValue("@valueType", discount.ValueType);
        command.Parameters.AddWithValue("@percentage", (object?)discount.Percentage ?? DBNull.Value);
        command.Parameters.AddWithValue("@fixedAmount", (object?)discount.FixedAmount ?? DBNull.Value);
        command.Parameters.AddWithValue("@startDate", (object?)discount.StartDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@endDate", (object?)discount.EndDate ?? DBNull.Value);
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
            StartDate = reader["fecha_inicio"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(reader["fecha_inicio"]),
            EndDate = reader["fecha_fin"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_fin"])
        };
    }
}
