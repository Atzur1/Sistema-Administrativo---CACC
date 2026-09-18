using EntityLibrary;
using Microsoft.Data.SqlClient;
using System.Linq;

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

        public Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio)
        {
            string query = @"
                SELECT PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado
                FROM PAGOS WITH (UPDLOCK, ROWLOCK)
                WHERE FK_id_jugador = @idJugador AND estado = 0
                  AND fecha_vencimiento IS NOT NULL AND MONTH(fecha_vencimiento) = @mes AND YEAR(fecha_vencimiento) = @anio";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            comando.Parameters.AddWithValue("@mes", mes);
            comando.Parameters.AddWithValue("@anio", anio);

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerPago(reader) : null;
        }

        // Solo reduce monto_final (el saldo que falta pagar). monto_base queda intacto a
        // propósito: conserva el monto ORIGINAL de la cuota para poder mostrar después
        // "debía $70.000, ya pagó $30.000, le faltan $40.000" en el detalle de deuda.
        public void ActualizarSaldoPendiente(SqlConnection conexion, SqlTransaction transaccion, int idPago, decimal nuevoMonto)
        {
            string query = @"
                UPDATE PAGOS SET monto_final = @monto
                WHERE PK_id_pago = @id AND estado = 0";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@monto", nuevoMonto);
            comando.Parameters.AddWithValue("@id", idPago);
            comando.ExecuteNonQuery();
        }

        public void EliminarPago(SqlConnection conexion, SqlTransaction transaccion, int idPago)
        {
            string query = "DELETE FROM PAGOS WHERE PK_id_pago = @id AND estado = 0";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@id", idPago);
            comando.ExecuteNonQuery();
        }

        // Genera la cuota de UN mes puntual (el que se le pida — típicamente el de vigente_desde
        // del arancel recién cargado), para cada jugador que no tenga ya una fila para ese
        // período. Es 100% manual: no hay ninguna noción de "mes actual" acá adentro — quien
        // llama a esto decide de qué mes es la cuota. Se puede llamar repetidas veces sin
        // duplicar nada (por el NOT EXISTS).
        public void GenerarCuotasPendientesDelMes(int mes, int anio)
        {
            // Autocontenido (abre su propia transacción): se llama desde ArancelesService justo
            // después de programar un arancel, no desde un flujo que ya tenga una transacción abierta.
            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();
            using SqlTransaction transaccion = conexion.BeginTransaction();

            try
            {
                string query = @"
                    DECLARE @maxId INT;
                    SELECT @maxId = ISNULL(MAX(PK_id_pago), 0) FROM PAGOS WITH (TABLOCKX, HOLDLOCK);

                    INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado)
                    SELECT
                        @maxId + ROW_NUMBER() OVER (ORDER BY j.PK_id_jugador),
                        j.PK_id_jugador,
                        arancel.monto,
                        descuento.PK_id_jugador_descuento,
                        CASE WHEN descuento.porcentaje IS NOT NULL
                             THEN arancel.monto - (arancel.monto * descuento.porcentaje / 100.0)
                             ELSE arancel.monto END,
                        NULL,
                        NULL,
                        @primerDiaMes,
                        0
                    FROM JUGADORES j
                    JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                    CROSS APPLY (
                        -- Un arancel cargado el 5 de enero cubre igual TODO enero, no solo desde
                        -- el día 5: por eso se compara contra el último día del mes del período
                        -- (EOMONTH), no contra el día 1 (@primerDiaMes).
                        SELECT TOP (1) monto FROM ARANCELES
                        WHERE genero = LTRIM(RTRIM(p.genero))
                          AND vigente_desde <= EOMONTH(@primerDiaMes)
                        ORDER BY vigente_desde DESC
                    ) AS arancel
                    OUTER APPLY (
                        SELECT TOP (1) jd.PK_id_jugador_descuento, td.porcentaje
                        FROM JUGADORES_DESCUENTOS jd
                        JOIN TIPO_DESCUENTO td ON jd.FK_id_descuento = td.PK_id_descuento
                        WHERE jd.FK_id_jugador = j.PK_id_jugador AND jd.estado_activo = 1
                          AND td.fecha_inicio <= EOMONTH(@primerDiaMes) AND td.fecha_fin >= @primerDiaMes
                        ORDER BY td.porcentaje DESC
                    ) AS descuento
                    WHERE NOT EXISTS (
                        SELECT 1 FROM PAGOS p2
                        WHERE p2.FK_id_jugador = j.PK_id_jugador
                          AND p2.fecha_vencimiento IS NOT NULL
                          AND MONTH(p2.fecha_vencimiento) = @mes AND YEAR(p2.fecha_vencimiento) = @anio
                    );";

                using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
                comando.Parameters.AddWithValue("@primerDiaMes", new DateTime(anio, mes, 1));
                comando.Parameters.AddWithValue("@mes", mes);
                comando.Parameters.AddWithValue("@anio", anio);
                comando.ExecuteNonQuery();

                transaccion.Commit();
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
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

            // Un abono (estado = 1) solo aparece acá si YA NO queda una cuota pendiente (estado =
            // 0) de ese mismo jugador para ese mismo período — o sea, si terminó de cubrir el
            // total. Mientras la cuota siga con saldo (pendiente todavía viva), el abono parcial
            // no se lista, aunque ya esté guardado en la base.
            //
            // Si una cuota se terminó de pagar en VARIOS abonos (ej. $20.000 + $50.000), acá tiene
            // que verse como UN solo pago de $70.000 — no dos líneas separadas. Por eso se agrupa
            // por jugador+período: se suman los montos, se toma la fecha del abono que la completó,
            // y su método de pago (el de la última fila del grupo) representa al conjunto.
            string query = @"
                ;WITH Completos AS (
                    SELECT pg.PK_id_pago, pg.FK_id_jugador, pg.metodo_pago,
                        SUM(pg.monto_final) OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento) AS monto_grupo,
                        MAX(pg.fecha_pago) OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento) AS fecha_grupo,
                        ROW_NUMBER() OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento ORDER BY pg.fecha_pago DESC, pg.PK_id_pago DESC) AS rn
                    FROM PAGOS pg
                    WHERE pg.estado = 1 AND pg.fecha_pago IS NOT NULL
                      AND NOT EXISTS (
                          SELECT 1 FROM PAGOS pendiente
                          WHERE pendiente.FK_id_jugador = pg.FK_id_jugador
                            AND pendiente.estado = 0
                            AND pendiente.fecha_vencimiento IS NOT NULL AND pg.fecha_vencimiento IS NOT NULL
                            AND MONTH(pendiente.fecha_vencimiento) = MONTH(pg.fecha_vencimiento)
                            AND YEAR(pendiente.fecha_vencimiento) = YEAR(pg.fecha_vencimiento)
                      )
                )
                SELECT TOP (@top) c.PK_id_pago, c.FK_id_jugador, p.nombre, p.apellido, c.metodo_pago,
                    c.monto_grupo AS monto_final, c.fecha_grupo AS fecha_pago
                FROM Completos c
                JOIN JUGADORES j ON c.FK_id_jugador = j.PK_id_jugador
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                WHERE c.rn = 1
                ORDER BY c.fecha_grupo DESC, c.PK_id_pago DESC";

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

        // Detalle de deuda de un jugador: cada cuota todavía pendiente (estado = 0), con lo que
        // ya abonó para ese período (estado = 1, mismo mes/año) y cuánto le sigue faltando.
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador)
        {
            var pendientes = new List<CuotaPendienteDetalle>();

            string queryPendientes = @"
                SELECT PK_id_pago, monto_base, monto_final, fecha_vencimiento
                FROM PAGOS
                WHERE FK_id_jugador = @idJugador AND estado = 0
                ORDER BY fecha_vencimiento";

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using (SqlCommand comando = new SqlCommand(queryPendientes, conexion))
            {
                comando.Parameters.AddWithValue("@idJugador", idJugador);
                using SqlDataReader reader = comando.ExecuteReader();
                while (reader.Read())
                {
                    var periodoBase = Convert.ToDateTime(reader["fecha_vencimiento"]);
                    pendientes.Add(new CuotaPendienteDetalle
                    {
                        IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                        Periodo = $"{MesesCompletos[periodoBase.Month - 1]} {periodoBase.Year}",
                        MontoOriginal = Convert.ToDecimal(reader["monto_base"]),
                        SaldoPendiente = Convert.ToDecimal(reader["monto_final"])
                    });
                }
            }

            if (pendientes.Count == 0)
            {
                return pendientes;
            }

            // Todos los abonos (estado = 1) del jugador, para matchear por período (mes/año de
            // fecha_vencimiento) contra cada cuota pendiente de arriba.
            string queryAbonos = @"
                SELECT monto_final, metodo_pago, fecha_pago, fecha_vencimiento
                FROM PAGOS
                WHERE FK_id_jugador = @idJugador AND estado = 1 AND fecha_vencimiento IS NOT NULL
                ORDER BY fecha_pago";

            using (SqlCommand comando = new SqlCommand(queryAbonos, conexion))
            {
                comando.Parameters.AddWithValue("@idJugador", idJugador);
                using SqlDataReader reader = comando.ExecuteReader();
                while (reader.Read())
                {
                    var fechaVencimiento = Convert.ToDateTime(reader["fecha_vencimiento"]);
                    var cuota = pendientes.FirstOrDefault(c =>
                        c.Periodo == $"{MesesCompletos[fechaVencimiento.Month - 1]} {fechaVencimiento.Year}");

                    cuota?.Abonos.Add(new AbonoDetalle
                    {
                        Monto = Convert.ToDecimal(reader["monto_final"]),
                        MetodoPago = reader["metodo_pago"].ToString()?.Trim() ?? "",
                        FechaPago = Convert.ToDateTime(reader["fecha_pago"])
                    });
                }
            }

            return pendientes;
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
            // Es un REGISTRO: si una cuota se pagó en varios abonos (ej. $20.000 + $50.000),
            // los dos tienen que quedar listados — nada se resume en un solo número. Pero se
            // agrupan bajo el mismo período (mismo mes), así se ve claro que son parte de una
            // misma cuota. La paginación es por período (10 meses por página), no por abono suelto.
            var resultado = new HistorialPagosResultado { Page = page, PageSize = pageSize };

            string queryPeriodos = @"
                ;WITH Agrupado AS (
                    SELECT PK_id_pago, fecha_vencimiento,
                        COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)) AS clave_periodo,
                        SUM(monto_final) OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE))) AS monto_grupo,
                        MAX(fecha_pago) OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE))) AS fecha_grupo,
                        ROW_NUMBER() OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)) ORDER BY fecha_pago DESC, PK_id_pago DESC) AS rn
                    FROM PAGOS
                    WHERE FK_id_jugador = @idJugador AND estado = 1
                )
                SELECT COUNT(*) OVER() AS total_count, clave_periodo, monto_grupo, fecha_grupo, fecha_vencimiento
                FROM Agrupado
                WHERE rn = 1
                ORDER BY fecha_grupo DESC
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            // (clave_periodo, PagoHistorialItem) en el orden en que vinieron, para después
            // completar cada uno con sus abonos sin perder el orden de fecha_grupo DESC.
            var itemsPorClave = new List<(DateTime clave, PagoHistorialItem item)>();

            using SqlConnection conexion = new SqlConnection(_cadenaConexion);
            conexion.Open();

            using (SqlCommand comando = new SqlCommand(queryPeriodos, conexion))
            {
                comando.Parameters.AddWithValue("@idJugador", idJugador);
                comando.Parameters.AddWithValue("@offset", (page - 1) * pageSize);
                comando.Parameters.AddWithValue("@pageSize", pageSize);

                using SqlDataReader reader = comando.ExecuteReader();
                while (reader.Read())
                {
                    resultado.Total = Convert.ToInt32(reader["total_count"]);

                    var clave = Convert.ToDateTime(reader["clave_periodo"]);
                    var periodoBase = reader["fecha_vencimiento"] != DBNull.Value
                        ? Convert.ToDateTime(reader["fecha_vencimiento"])
                        : clave; // fallback: pagos históricos sin fecha_vencimiento cargada

                    var item = new PagoHistorialItem
                    {
                        Periodo = $"{MesesCompletos[periodoBase.Month - 1]} {periodoBase.Year}",
                        MontoTotal = Convert.ToDecimal(reader["monto_grupo"])
                    };
                    itemsPorClave.Add((clave, item));
                    resultado.Items.Add(item);
                }
            }

            // Los abonos de cada período de esta página, todos de una.
            foreach (var (clave, item) in itemsPorClave)
            {
                string queryAbonos = @"
                    SELECT monto_final, metodo_pago, fecha_pago
                    FROM PAGOS
                    WHERE FK_id_jugador = @idJugador AND estado = 1
                      AND COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)) = @clavePeriodo
                    ORDER BY fecha_pago, PK_id_pago";

                using SqlCommand comando = new SqlCommand(queryAbonos, conexion);
                comando.Parameters.AddWithValue("@idJugador", idJugador);
                comando.Parameters.AddWithValue("@clavePeriodo", clave);

                using SqlDataReader reader = comando.ExecuteReader();
                while (reader.Read())
                {
                    item.Abonos.Add(new PagoHistorialAbono
                    {
                        Monto = Convert.ToDecimal(reader["monto_final"]),
                        MetodoPago = reader["metodo_pago"].ToString()?.Trim() ?? "",
                        FechaPago = Convert.ToDateTime(reader["fecha_pago"])
                    });
                }
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
