using EntityLibrary;
using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    public class PagosDao : IPagosDao
    {
        private static readonly string[] MesesCompletos =
        {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        private readonly string _cadenaConexion;

        public PagosDao(string cadenaConexion)
        {
            _cadenaConexion = cadenaConexion;
        }

        // "Período" = la cuota que se está pagando (mapeada a fecha_vencimiento), NO fecha_pago
        // (que es cuándo se procesó el cobro y puede ser cualquier mes, ej. pagar diciembre en
        // septiembre). Comparar contra fecha_pago mezclaría ambos conceptos y dejaría pasar
        // duplicados del mismo período pagados en fechas distintas.
        public Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio)
        {
            string query = @"
                SELECT PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado
                FROM PAGOS WITH (UPDLOCK, ROWLOCK)
                WHERE FK_id_jugador = @idJugador AND estado = 1
                  AND fecha_vencimiento IS NOT NULL AND MONTH(fecha_vencimiento) = @mes AND YEAR(fecha_vencimiento) = @anio";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            comando.Parameters.AddWithValue("@mes", mes);
            comando.Parameters.AddWithValue("@anio", anio);

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerPago(reader) : null;
        }

        // PAGOS.PK_id_pago no tiene IDENTITY. TABLOCKX+HOLDLOCK sobre el cálculo del próximo id
        // serializa inserts concurrentes dentro de la transacción, evitando que dos cobros
        // simultáneos calculen el mismo id (el lock se libera recién al commit/rollback).
        public int InsertarPago(SqlConnection conexion, SqlTransaction transaccion, Pago pago)
        {
            string queryId = "SELECT ISNULL(MAX(PK_id_pago), 0) + 1 FROM PAGOS WITH (TABLOCKX, HOLDLOCK)";
            int nuevoId;
            using (SqlCommand comandoId = new SqlCommand(queryId, conexion, transaccion))
            {
                nuevoId = (int)comandoId.ExecuteScalar();
            }

            string queryInsert = @"
                INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado)
                VALUES (@id, @idJugador, @montoBase, @idJugadorDescuento, @montoFinal, @fechaPago, @metodoPago, @fechaVencimiento, @estado)";

            using SqlCommand comando = new SqlCommand(queryInsert, conexion, transaccion);
            comando.Parameters.AddWithValue("@id", nuevoId);
            comando.Parameters.AddWithValue("@idJugador", pago.IdJugador);
            comando.Parameters.AddWithValue("@montoBase", pago.MontoBase);
            comando.Parameters.AddWithValue("@idJugadorDescuento", (object?)pago.IdJugadorDescuento ?? DBNull.Value);
            comando.Parameters.AddWithValue("@montoFinal", pago.MontoFinal);
            comando.Parameters.AddWithValue("@fechaPago", (object?)pago.FechaPago ?? DBNull.Value);
            comando.Parameters.AddWithValue("@metodoPago", (object?)pago.MetodoPago ?? DBNull.Value);
            comando.Parameters.AddWithValue("@fechaVencimiento", (object?)pago.FechaVencimiento ?? DBNull.Value);
            comando.Parameters.AddWithValue("@estado", pago.Estado);

            comando.ExecuteNonQuery();
            return nuevoId;
        }

        public IReadOnlyList<Pago> ObtenerPagosPorId(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago)
        {
            var ids = idsPago.ToList();
            var resultado = new List<Pago>(ids.Count);
            if (ids.Count == 0)
            {
                return resultado;
            }

            var (clausulaIn, parametros) = ConstruirClausulaIn("id", ids);
            string query = $@"
                SELECT PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado
                FROM PAGOS WITH (UPDLOCK, ROWLOCK)
                WHERE PK_id_pago IN ({clausulaIn})";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddRange(parametros.ToArray());

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(LeerPago(reader));
            }

            return resultado;
        }

        public void MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago)
        {
            var ids = idsPago.ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var (clausulaIn, parametros) = ConstruirClausulaIn("id", ids);
            string query = $@"
                UPDATE PAGOS
                SET estado = 1, fecha_pago = @fechaPago, metodo_pago = @metodoPago
                WHERE PK_id_pago IN ({clausulaIn}) AND estado = 0";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@fechaPago", fechaPago);
            comando.Parameters.AddWithValue("@metodoPago", metodoPago);
            comando.Parameters.AddRange(parametros.ToArray());

            int filasAfectadas = comando.ExecuteNonQuery();
            if (filasAfectadas != ids.Count)
            {
                throw new InvalidOperationException(
                    $"Se esperaba actualizar {ids.Count} pago(s) y se actualizaron {filasAfectadas}.");
            }
        }

        public IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados()
        {
            var resultado = new List<PendienteJugador>();

            string query = @"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, c.nombre_categoria,
                       SUM(pg.monto_final) AS monto_total, COUNT(*) AS cantidad_cuotas
                FROM PAGOS pg
                JOIN JUGADORES j ON pg.FK_id_jugador = j.PK_id_jugador
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                WHERE pg.estado = 0
                GROUP BY j.PK_id_jugador, p.nombre, p.apellido, c.nombre_categoria
                ORDER BY monto_total DESC";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new PendienteJugador
                {
                    IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
                    NombreCompleto = $"{reader["apellido"].ToString()?.Trim()}, {reader["nombre"].ToString()?.Trim()}",
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    MontoTotal = Convert.ToDecimal(reader["monto_total"]),
                    CantidadCuotas = Convert.ToInt32(reader["cantidad_cuotas"])
                });
            }

            return resultado;
        }

        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top)
        {
            var resultado = new List<PagoReciente>();

            string query = @"
                SELECT TOP (@top) pg.PK_id_pago, pg.FK_id_jugador, p.nombre, p.apellido, pg.metodo_pago, pg.monto_final, pg.fecha_pago
                FROM PAGOS pg
                JOIN JUGADORES j ON pg.FK_id_jugador = j.PK_id_jugador
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                WHERE pg.estado = 1 AND pg.fecha_pago IS NOT NULL
                ORDER BY pg.fecha_pago DESC, pg.PK_id_pago DESC";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@top", top);

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new PagoReciente
                {
                    IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                    IdJugador = Convert.ToInt32(reader["FK_id_jugador"]),
                    NombreCompleto = $"{reader["apellido"].ToString()?.Trim()}, {reader["nombre"].ToString()?.Trim()}",
                    MetodoPago = reader["metodo_pago"].ToString()?.Trim() ?? "",
                    Monto = Convert.ToDecimal(reader["monto_final"]),
                    FechaPago = Convert.ToDateTime(reader["fecha_pago"])
                });
            }

            return resultado;
        }

        public ResumenPagos ObtenerResumen()
        {
            string query = @"
                SELECT
                    (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS WHERE estado = 1 AND YEAR(fecha_pago) = YEAR(GETDATE())) AS recaudado_anio,
                    (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND YEAR(fecha_pago) = YEAR(GETDATE()) AND MONTH(fecha_pago) = MONTH(GETDATE())) AS pagos_del_mes,
                    (SELECT COUNT(*) FROM PAGOS WHERE estado = 0) AS cantidad_pendientes";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            reader.Read();

            return new ResumenPagos
            {
                RecaudadoAnioActual = Convert.ToDecimal(reader["recaudado_anio"]),
                PagosDelMes = Convert.ToInt32(reader["pagos_del_mes"]),
                CantidadPendientes = Convert.ToInt32(reader["cantidad_pendientes"])
            };
        }

        // Historial paginado de pagos abonados de un jugador, más reciente primero.
        // COUNT(*) OVER() trae el total de filas junto con la página pedida en una sola consulta,
        // evitando un segundo round-trip solo para saber cuántas páginas hay.
        public HistorialPagosResultado ObtenerHistorialPagos(int idJugador, int page, int pageSize)
        {
            string query = @"
                SELECT COUNT(*) OVER() AS total_count, PK_id_pago, monto_final, fecha_pago, metodo_pago, fecha_vencimiento
                FROM PAGOS
                WHERE FK_id_jugador = @idJugador AND estado = 1
                ORDER BY fecha_pago DESC, PK_id_pago DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            comando.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
            comando.Parameters.AddWithValue("@pageSize", pageSize);

            var resultado = new HistorialPagosResultado { Page = page, PageSize = pageSize };

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Total = Convert.ToInt32(reader["total_count"]);

                DateTime fechaPago = Convert.ToDateTime(reader["fecha_pago"]);
                DateTime? vencimiento = reader["fecha_vencimiento"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_vencimiento"]) : null;

                // Los 1258 pagos históricos originales nunca tuvieron fecha_vencimiento cargada.
                // Sin este fallback, "Período" mostraría "-" en casi todo el historial: se deriva
                // de fecha_pago (asumiendo que históricamente se pagaba el mismo mes de la cuota).
                DateTime periodoBase = vencimiento ?? fechaPago;

                resultado.Items.Add(new PagoHistorialItem
                {
                    IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                    FechaPago = fechaPago,
                    Periodo = $"{MesesCompletos[periodoBase.Month - 1]} {periodoBase.Year}",
                    Monto = Convert.ToDecimal(reader["monto_final"]),
                    MetodoPago = reader["metodo_pago"].ToString()?.Trim() ?? ""
                });
            }

            return resultado;
        }

        private static Pago LeerPago(SqlDataReader reader) => new()
        {
            IdPago = Convert.ToInt32(reader["PK_id_pago"]),
            IdJugador = Convert.ToInt32(reader["FK_id_jugador"]),
            MontoBase = Convert.ToDecimal(reader["monto_base"]),
            IdJugadorDescuento = reader["FK_id_jugador_descuento"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_jugador_descuento"]) : null,
            MontoFinal = Convert.ToDecimal(reader["monto_final"]),
            FechaPago = reader["fecha_pago"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_pago"]) : null,
            MetodoPago = reader["metodo_pago"] != DBNull.Value ? reader["metodo_pago"].ToString() : null,
            FechaVencimiento = reader["fecha_vencimiento"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_vencimiento"]) : null,
            Estado = Convert.ToBoolean(reader["estado"])
        };

        // Arma "IN (@id0, @id1, ...)" parametrizado: evita inyección SQL y listas de tamaño variable
        // no soportadas directamente por SqlCommand.Parameters.
        private static (string clausula, List<SqlParameter> parametros) ConstruirClausulaIn(string prefijo, List<int> valores)
        {
            var nombres = new List<string>(valores.Count);
            var parametros = new List<SqlParameter>(valores.Count);

            for (int i = 0; i < valores.Count; i++)
            {
                string nombre = $"@{prefijo}{i}";
                nombres.Add(nombre);
                parametros.Add(new SqlParameter(nombre, valores[i]));
            }

            return (string.Join(", ", nombres), parametros);
        }
    }
}
