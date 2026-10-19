using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    // Meses que quedaron con cuotas sin generar. Usa las mismas reglas que el generador (SqlCuotaMensual):
    // jugador en el club ese mes, con un arancel vigente y sin ninguna fila de cuota del mes.
    public class CuotasFaltantesDao : ICuotasFaltantesDao
    {
        private readonly string _cadenaConexion;

        public CuotasFaltantesDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        public IReadOnlyList<MesConCuotasFaltantes> ObtenerMesesConFaltantes(DateTime desde, DateTime hasta)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            return ObtenerMesesConFaltantesCore(conexion, null, desde, hasta);
        }

        internal static IReadOnlyList<MesConCuotasFaltantes> ObtenerMesesConFaltantesCore(SqlConnection conexion, SqlTransaction? transaccion, DateTime desde, DateTime hasta)
        {
            // Una fila por mes del rango (a lo sumo unos pocos: el servicio pide los últimos 12).
            string query = $@"
                WITH meses AS (
                    SELECT CAST(@desde AS DATE) AS mes
                    UNION ALL
                    SELECT DATEADD(MONTH, 1, mes) FROM meses WHERE mes < @hasta
                )
                SELECT m.mes, COUNT(*) AS cantidad, SUM(arancel.monto) AS monto_total
                FROM meses m
                JOIN JUGADORES j ON {SqlCuotaMensual.JugadorEnElClub("m.mes")}
                JOIN PERSONAS p ON j.FK_id_persona = p.PK_id_persona
                CROSS APPLY ({SqlCuotaMensual.ArancelDelJugador("m.mes")}) AS arancel
                WHERE {SqlCuotaMensual.SinCuotaEnElMes("m.mes")}
                GROUP BY m.mes
                ORDER BY m.mes
                OPTION (MAXRECURSION 120)";

            var resultado = new List<MesConCuotasFaltantes>();
            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@desde", System.Data.SqlDbType.Date).Value = desde.Date;
            comando.Parameters.Add("@hasta", System.Data.SqlDbType.Date).Value = hasta.Date;
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new MesConCuotasFaltantes
                {
                    Periodo = Convert.ToDateTime(reader["mes"]),
                    Cantidad = Convert.ToInt32(reader["cantidad"]),
                    MontoTotal = Convert.ToDecimal(reader["monto_total"])
                });
            }
            return resultado;
        }

        public IReadOnlyList<CuotaFaltante> ObtenerFaltantesDelMes(DateTime periodo)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            return ObtenerFaltantesDelMesCore(conexion, null, periodo);
        }

        internal static IReadOnlyList<CuotaFaltante> ObtenerFaltantesDelMesCore(SqlConnection conexion, SqlTransaction? transaccion, DateTime periodo)
        {
            string query = $@"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, c.nombre_categoria, arancel.monto
                FROM JUGADORES j
                JOIN PERSONAS p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                CROSS APPLY ({SqlCuotaMensual.ArancelDelJugador("@mes")}) AS arancel
                WHERE {SqlCuotaMensual.JugadorEnElClub("@mes")}
                  AND {SqlCuotaMensual.SinCuotaEnElMes("@mes")}
                ORDER BY p.apellido, p.nombre";

            var resultado = new List<CuotaFaltante>();
            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@mes", System.Data.SqlDbType.Date).Value = periodo.Date;
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new CuotaFaltante
                {
                    IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
                    NombreCompleto = $"{reader["apellido"].ToString()?.Trim()}, {reader["nombre"].ToString()?.Trim()}",
                    Dni = reader["Dni"].ToString()?.Trim() ?? "",
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    Monto = Convert.ToDecimal(reader["monto"])
                });
            }
            return resultado;
        }

        public int GenerarFaltantesDelMes(DateTime periodo)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlTransaction transaccion = conexion.BeginTransaction();
            BloqueoEscrituraPagos.Tomar(conexion, transaccion);
            try
            {
                int creadas = SqlCuotaMensual.Generar(conexion, transaccion, null, null, null, periodo.Month, periodo.Year);
                transaccion.Commit();
                return creadas;
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }
    }
}
