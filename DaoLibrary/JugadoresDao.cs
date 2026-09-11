using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public class JugadoresDao : IJugadoresDao
    {
        private readonly string _cadenaConexion;

        public JugadoresDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public IReadOnlyList<JugadorResumen> ListarJugadores()
        {
            var resultado = new List<JugadorResumen>();

            string query = @"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, p.genero, c.nombre_categoria
                FROM JUGADORES j
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                ORDER BY p.apellido, p.nombre";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new JugadorResumen
                {
                    IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
                    Nombre = reader["nombre"].ToString()?.Trim() ?? "",
                    Apellido = reader["apellido"].ToString()?.Trim() ?? "",
                    Dni = reader["Dni"].ToString()?.Trim() ?? "",
                    Genero = reader["genero"].ToString()?.Trim() ?? "",
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? ""
                });
            }

            return resultado;
        }

        public JugadorResumen? ObtenerJugadorPorId(int idJugador)
        {
            string query = @"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, p.genero, c.nombre_categoria
                FROM JUGADORES j
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                WHERE j.PK_id_jugador = @idJugador";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);

            using SqlDataReader reader = comando.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new JugadorResumen
            {
                IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
                Nombre = reader["nombre"].ToString()?.Trim() ?? "",
                Apellido = reader["apellido"].ToString()?.Trim() ?? "",
                Dni = reader["Dni"].ToString()?.Trim() ?? "",
                Genero = reader["genero"].ToString()?.Trim() ?? "",
                Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? ""
            };
        }
    }
}
