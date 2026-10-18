using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary;

public interface IJugadoresSinGeneroDao
{
    JugadoresSinGenero Obtener(int maximo);
}

// Aviso del Resumen General: jugadores que siguen en el club y no tienen un género válido. Los jugadores dados de
// baja no cuentan (ya no reciben cuotas). El género de una persona lo carga quien la da de alta.
public class JugadoresSinGeneroDao : IJugadoresSinGeneroDao
{
    private readonly string _cadenaConexion;

    public JugadoresSinGeneroDao(string cadenaConexion) => _cadenaConexion = cadenaConexion;

    public JugadoresSinGenero Obtener(int maximo)
    {
        using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
        return ObtenerCore(conexion, null, maximo);
    }

    internal static JugadoresSinGenero ObtenerCore(SqlConnection conexion, SqlTransaction? transaccion, int maximo)
    {
        // Los valores válidos salen de Generos (una sola definición); son constantes del código, no entrada del usuario.
        string validos = string.Join(", ", Generos.Validos.Select(g => $"'{g}'"));
        string query = $@"
            SELECT COUNT(*) OVER () AS total, j.PK_id_jugador, p.nombre, p.apellido, p.dni, c.nombre_categoria
            FROM JUGADORES j
            JOIN PERSONAS p ON p.PK_id_persona = j.FK_id_persona
            LEFT JOIN CATEGORIAS c ON c.PK_id_categoria = j.FK_id_categoria
            WHERE j.fecha_baja IS NULL
              AND (p.genero IS NULL OR LTRIM(RTRIM(p.genero)) NOT IN ({validos}))
            ORDER BY p.apellido, p.nombre, j.PK_id_jugador
            OFFSET 0 ROWS FETCH NEXT @maximo ROWS ONLY";

        using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
        comando.Parameters.Add("@maximo", System.Data.SqlDbType.Int).Value = Math.Max(1, maximo);
        var jugadores = new List<JugadorSinGenero>();
        int total = 0;
        using SqlDataReader reader = comando.ExecuteReader();
        while (reader.Read())
        {
            total = Convert.ToInt32(reader["total"]);
            jugadores.Add(new JugadorSinGenero(
                Convert.ToInt32(reader["PK_id_jugador"]),
                $"{(reader["apellido"].ToString() ?? "").Trim()}, {(reader["nombre"].ToString() ?? "").Trim()}",
                (reader["dni"].ToString() ?? "").Trim(),
                (reader["nombre_categoria"].ToString() ?? "").Trim()));
        }
        return new JugadoresSinGenero(total, jugadores);
    }
}
