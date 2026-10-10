using Microsoft.Data.SqlClient;
using EntityLibrary;

namespace DaoLibrary
{
    public interface ICategoriasDao
    {
        IReadOnlyList<Categoria> ObtenerTodas();
    }

    // HU-020: catálogo completo de categorías, independiente de quién debe o no.
    // Antes el combo de "Deudas y Morosidad" se armaba con las categorías del
    // propio padrón de deudores, así que una categoría sin morosos actualmente
    // nunca aparecía para elegir (rechazo de QA, dos veces). Esta consulta lee
    // CATEGORIAS directamente, sin pasar por PAGOS/JUGADORES.
    public class CategoriasDao : ICategoriasDao
    {
        private readonly string _cadenaConexion;

        public CategoriasDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public IReadOnlyList<Categoria> ObtenerTodas()
        {
            var resultado = new List<Categoria>();
            const string query = "SELECT PK_id_categoria, nombre_categoria FROM CATEGORIAS ORDER BY nombre_categoria";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new Categoria
                {
                    IdCategoria = Convert.ToInt32(reader["PK_id_categoria"]),
                    Nombre = reader["nombre_categoria"].ToString()?.Trim() ?? ""
                });
            }

            return resultado;
        }
    }
}
