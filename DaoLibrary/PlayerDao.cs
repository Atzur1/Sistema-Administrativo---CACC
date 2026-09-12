namespace DaoLibrary;

using Microsoft.Data.SqlClient;
using EntityLibrary;

public class PlayerDao
{
    private readonly string _connectionString;

    public PlayerDao(string connectionString)
    {
        _connectionString = connectionString;
    }

    // The lookup field filters on the client side, so the whole squad is sent
    // once instead of hitting the API on every keystroke.
    public List<Player> GetAllPlayers()
    {
        List<Player> players = new List<Player>();
        string query = @"
            SELECT
                j.PK_id_jugador,
                ISNULL(p.nombre, '') AS nombre,
                ISNULL(p.apellido, '') AS apellido,
                ISNULL(p.Dni, '') AS dni,
                ISNULL(c.nombre_categoria, '') AS categoria
            FROM JUGADORES j
                INNER JOIN PERSONA p ON p.PK_id_persona = j.FK_id_persona
                LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
            ORDER BY p.apellido, p.nombre;";

        using (SqlConnection connection = new SqlConnection(_connectionString))
        {
            connection.Open();

            using (SqlCommand command = new SqlCommand(query, connection))
            {
                using (SqlDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        players.Add(new Player
                        {
                            Id = Convert.ToInt64(reader["PK_id_jugador"]),
                            FirstName = reader["nombre"].ToString() ?? "",
                            LastName = reader["apellido"].ToString() ?? "",
                            Document = reader["dni"].ToString() ?? "",
                            Category = reader["categoria"].ToString() ?? ""
                        });
                    }
                }
            }
        }

        return players;
    }
}
