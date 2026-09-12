namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

public class DiscountDao
{
    private readonly string _connectionString;

    // A discount is active when it has not been deactivated and today falls inside
    // its date range. It is resolved here, in the engine, so the grid can get every
    // label in a single query.
    private const string BaseQuery = @"
        SELECT
            jd.PK_id_jugador_descuento,
            jd.FK_id_jugador,
            ISNULL(p.apellido, '') + ', ' + ISNULL(p.nombre, '') AS jugador,
            ISNULL(c.nombre_categoria, '') AS categoria,
            ISNULL(td.tipo_descuento, '') AS tipo_descuento,
            ISNULL(td.porcentaje, 0) AS porcentaje,
            td.fecha_inicio,
            td.fecha_fin
        FROM JUGADORES_DESCUENTOS jd
            INNER JOIN JUGADORES j ON j.PK_id_jugador = jd.FK_id_jugador
            INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
            INNER JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
        WHERE jd.estado_activo = 1
        AND CAST(GETDATE() AS DATE) BETWEEN td.fecha_inicio AND td.fecha_fin";

    public DiscountDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<Discount> GetActiveDiscounts()
    {
        List<Discount> discounts = new List<Discount>();
        string query = BaseQuery + " ORDER BY jd.FK_id_jugador;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
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

    public Discount? GetActiveDiscountByPlayer(long playerId)
    {
        Discount? foundDiscount = null;
        string query = BaseQuery + " AND jd.FK_id_jugador = @playerId;";

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
                    }
                }
            }
        }

        return foundDiscount;
    }

    // The administration table lists every assignment ever granted, current and
    // expired alike, so it cannot reuse BaseQuery: that one filters by validity.
    // IsActive is resolved here with the same criteria the badge uses.
    public List<Discount> GetAllDiscounts()
    {
        List<Discount> discounts = new List<Discount>();
        string query = @"
            SELECT
                jd.PK_id_jugador_descuento,
                jd.FK_id_jugador,
                ISNULL(p.apellido, '') + ', ' + ISNULL(p.nombre, '') AS jugador,
                ISNULL(c.nombre_categoria, '') AS categoria,
                ISNULL(td.tipo_descuento, '') AS tipo_descuento,
                ISNULL(td.porcentaje, 0) AS porcentaje,
                td.fecha_inicio,
                td.fecha_fin,
                CASE WHEN ISNULL(jd.estado_activo, 0) = 1
                     AND CAST(GETDATE() AS DATE) BETWEEN td.fecha_inicio AND td.fecha_fin
                     THEN 1 ELSE 0 END AS vigente
            FROM JUGADORES_DESCUENTOS jd
                INNER JOIN JUGADORES j ON j.PK_id_jugador = jd.FK_id_jugador
                INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
                INNER JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
                LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
            ORDER BY vigente DESC, td.fecha_fin DESC;";

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

    // Every row coming out of BaseQuery already passed the active filter
    private Discount MapDiscount(SqlDataReader reader)
    {
        return new Discount
        {
            Id = Convert.ToInt64(reader["PK_id_jugador_descuento"]),
            PlayerId = Convert.ToInt64(reader["FK_id_jugador"]),
            PlayerName = reader["jugador"].ToString() ?? "",
            Category = reader["categoria"].ToString() ?? "",
            Type = reader["tipo_descuento"].ToString() ?? "",
            Percentage = Convert.ToInt32(reader["porcentaje"]),
            StartDate = Convert.ToDateTime(reader["fecha_inicio"]),
            EndDate = Convert.ToDateTime(reader["fecha_fin"]),
            IsActive = true
        };
    }
}
