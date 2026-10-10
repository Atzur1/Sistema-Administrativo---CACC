using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public enum BajaJugadorResultado { Ok, NoEncontrado, YaDadoDeBaja, FechaInvalida }

    public class JugadoresDao : IJugadoresDao
    {
        private readonly string _cadenaConexion;

        private const string SelectJugador = @"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, p.genero, c.nombre_categoria, j.fecha_alta, j.fecha_baja
                FROM JUGADORES j
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria";

        public JugadoresDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public IReadOnlyList<JugadorResumen> ListarJugadores()
        {
            var resultado = new List<JugadorResumen>();

            string query = SelectJugador + @"
                ORDER BY p.apellido, p.nombre";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(LeerJugador(reader));
            }

            return resultado;
        }

        public JugadorResumen? ObtenerJugadorPorId(int idJugador)
        {
            string query = SelectJugador + @"
                WHERE j.PK_id_jugador = @idJugador";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerJugador(reader) : null;
        }

        // Da de baja a un jugador desde esa fecha: el generador deja de emitirle cuotas a partir del mes
        // siguiente. Las cuotas pendientes SIN pagos de meses posteriores al de la baja se borran (por
        // ejemplo, una baja que se carga tarde, o un mes futuro que se había emitido para pagar por
        // adelantado); las que ya tienen algún pago y las de meses anteriores quedan como deuda real.
        public (BajaJugadorResultado Resultado, int CuotasEliminadas) DarDeBaja(int idJugador, DateTime fechaBaja)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlTransaction transaccion = conexion.BeginTransaction(System.Data.IsolationLevel.Serializable);
            BloqueoEscrituraPagos.Tomar(conexion, transaccion);

            DateTime? fechaAlta;
            using (var buscar = new SqlCommand(
                "SELECT fecha_alta, fecha_baja FROM JUGADORES WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_jugador = @id",
                conexion, transaccion))
            {
                buscar.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                using var reader = buscar.ExecuteReader();
                if (!reader.Read())
                {
                    reader.Close();
                    transaccion.Rollback();
                    return (BajaJugadorResultado.NoEncontrado, 0);
                }
                if (reader["fecha_baja"] != DBNull.Value)
                {
                    reader.Close();
                    transaccion.Rollback();
                    return (BajaJugadorResultado.YaDadoDeBaja, 0);
                }
                fechaAlta = reader["fecha_alta"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_alta"]) : null;
            }

            if (fechaAlta.HasValue && fechaBaja.Date < fechaAlta.Value.Date)
            {
                transaccion.Rollback();
                return (BajaJugadorResultado.FechaInvalida, 0);
            }

            using (var baja = new SqlCommand("UPDATE JUGADORES SET fecha_baja = @fecha WHERE PK_id_jugador = @id", conexion, transaccion))
            {
                baja.Parameters.Add("@fecha", System.Data.SqlDbType.Date).Value = fechaBaja.Date;
                baja.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                baja.ExecuteNonQuery();
            }

            int eliminadas;
            using (var limpiar = new SqlCommand(@"
                DELETE p
                FROM PAGOS p
                WHERE p.FK_id_jugador = @id AND p.concepto = 'Cuota' AND p.estado = 0
                  AND p.fecha_vencimiento > EOMONTH(@fecha)
                  AND NOT EXISTS (
                      SELECT 1 FROM PAGOS q
                      WHERE q.FK_id_jugador = p.FK_id_jugador AND q.concepto = 'Cuota' AND q.estado = 1
                        AND YEAR(q.fecha_vencimiento) = YEAR(p.fecha_vencimiento)
                        AND MONTH(q.fecha_vencimiento) = MONTH(p.fecha_vencimiento))", conexion, transaccion))
            {
                limpiar.Parameters.Add("@fecha", System.Data.SqlDbType.Date).Value = fechaBaja.Date;
                limpiar.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
                eliminadas = limpiar.ExecuteNonQuery();
            }

            transaccion.Commit();
            return (BajaJugadorResultado.Ok, eliminadas);
        }

        // Vuelve a dejar activo a un jugador dado de baja. Las cuotas de los meses en que estuvo afuera
        // no se generan (el generador solo emite el mes en curso).
        public bool Reactivar(int idJugador)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlCommand comando = new SqlCommand(
                "UPDATE JUGADORES SET fecha_baja = NULL WHERE PK_id_jugador = @id AND fecha_baja IS NOT NULL", conexion);
            comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idJugador;
            return comando.ExecuteNonQuery() == 1;
        }

        private static JugadorResumen LeerJugador(SqlDataReader reader) => new()
        {
            IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
            Nombre = reader["nombre"].ToString()?.Trim() ?? "",
            Apellido = reader["apellido"].ToString()?.Trim() ?? "",
            Dni = reader["Dni"].ToString()?.Trim() ?? "",
            Genero = reader["genero"].ToString()?.Trim() ?? "",
            Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
            FechaAlta = reader["fecha_alta"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_alta"]) : null,
            FechaBaja = reader["fecha_baja"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_baja"]) : null
        };
    }
}
