using EntityLibrary;
using Microsoft.Data.SqlClient;
using System.Linq;

namespace DaoLibrary
{
    public class PagosDao : IPagosDao, ICuotasPorArancelDao
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
        //
        // HU-033: solo cuotas. La inscripción de un jugador cae en el mes de su alta, y sin este
        // filtro cobrar la cuota de ese mes podría tomar la inscripción.
        public Pago? ObtenerPagoAbonadoDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio)
        {
            string query = @"
                SELECT PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado
                FROM PAGOS WITH (UPDLOCK, HOLDLOCK)
                WHERE FK_id_jugador = @idJugador AND estado = 1 AND concepto = 'Cuota'
                  AND fecha_vencimiento >= @inicio AND fecha_vencimiento < @fin";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            comando.Parameters.AddWithValue("@inicio", new DateTime(anio, mes, 1));
            comando.Parameters.AddWithValue("@fin", new DateTime(anio, mes, 1).AddMonths(1));

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerPago(reader) : null;
        }

        public Pago? ObtenerPagoPendienteDeJugadorEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio)
        {
            string query = @"
                SELECT PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado
                FROM PAGOS WITH (UPDLOCK, HOLDLOCK)
                WHERE FK_id_jugador = @idJugador AND estado = 0 AND concepto = 'Cuota'
                  AND fecha_vencimiento >= @inicio AND fecha_vencimiento < @fin";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            comando.Parameters.AddWithValue("@inicio", new DateTime(anio, mes, 1));
            comando.Parameters.AddWithValue("@fin", new DateTime(anio, mes, 1).AddMonths(1));

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerPago(reader) : null;
        }

        // Mismo criterio que el resto de las lecturas: el OUTER APPLY de DescuentosSql, aplicado a una
        // fila armada con el jugador y el período (así hay una sola definición de qué beneficio cubre
        // una cuota). Un beneficio cancelado sigue aplicando a un período que venció el mismo día de la
        // cancelación o antes; para períodos posteriores, no.
        public DescuentoAplicable? ObtenerDescuentoAplicableEnPeriodo(SqlConnection conexion, SqlTransaction transaccion, int idJugador, DateTime fechaVencimiento)
        {
            string query = $@"
                SELECT d.PK_id_jugador_descuento, td.tipo_descuento AS motivo, d.tipo_valor, d.porcentaje, d.monto_fijo
                FROM (SELECT @idJugador AS FK_id_jugador, @fechaVencimiento AS fecha_vencimiento, CAST('Cuota' AS VARCHAR(20)) AS concepto) pg
                {DescuentosSql.ApplyDescuentoActivo}
                JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = d.FK_id_descuento";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@idJugador", System.Data.SqlDbType.Int).Value = idJugador;
            comando.Parameters.Add("@fechaVencimiento", System.Data.SqlDbType.Date).Value = fechaVencimiento.Date;

            using SqlDataReader reader = comando.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            return new DescuentoAplicable
            {
                IdJugadorDescuento = Convert.ToInt32(reader["PK_id_jugador_descuento"]),
                Motivo = reader["motivo"].ToString()?.Trim() ?? "",
                TipoValor = reader["tipo_valor"].ToString()?.Trim() ?? "",
                Porcentaje = reader["porcentaje"] != DBNull.Value ? Convert.ToDecimal(reader["porcentaje"]) : null,
                MontoFijo = reader["monto_fijo"] != DBNull.Value ? Convert.ToDecimal(reader["monto_fijo"]) : null
            };
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

        // Filtros compartidos por ContarCuotasConPagos y EliminarCuotasPendientesSinPagos: jugadores
        // alcanzados (género o categoría), período (fecha_vencimiento) y, si se informa, monto emitido.
        private const string FiltroCuotasDelArancel = @"
            p.concepto = 'Cuota'
            AND p.fecha_vencimiento >= @desde
            AND (@hasta IS NULL OR p.fecha_vencimiento < @hasta)
            AND (@genero IS NULL OR LTRIM(RTRIM(pe.genero)) = @genero)
            AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)";

        // Misma cuota = mismo jugador y mismo mes de fecha_vencimiento. Tiene pagos si alguna de sus filas
        // está abonada (estado = 1): los abonos parciales y el cobro total marcan filas así.
        private const string CuotaTienePagos = @"
            EXISTS (
                SELECT 1 FROM PAGOS q
                WHERE q.FK_id_jugador = p.FK_id_jugador AND q.concepto = 'Cuota' AND q.estado = 1
                  AND YEAR(q.fecha_vencimiento) = YEAR(p.fecha_vencimiento)
                  AND MONTH(q.fecha_vencimiento) = MONTH(p.fecha_vencimiento))";

        // Los abonos arrastran el monto_base de su cuota, así que filtrar por monto identifica a todas las
        // filas de las cuotas emitidas con un arancel, pendientes o abonadas.
        private const string CuotaEmitidaConMonto = @"
            EXISTS (
                SELECT 1 FROM PAGOS q
                WHERE q.FK_id_jugador = p.FK_id_jugador AND q.concepto = 'Cuota' AND q.monto_base = @montoBase
                  AND YEAR(q.fecha_vencimiento) = YEAR(p.fecha_vencimiento)
                  AND MONTH(q.fecha_vencimiento) = MONTH(p.fecha_vencimiento))";

        private static void CargarParametrosCuotas(SqlCommand comando, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            comando.Parameters.Add("@genero", System.Data.SqlDbType.VarChar, 20).Value = (object?)genero ?? DBNull.Value;
            comando.Parameters.Add("@idCategoria", System.Data.SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
            comando.Parameters.Add("@desde", System.Data.SqlDbType.Date).Value = desde.Date;
            comando.Parameters.Add("@hasta", System.Data.SqlDbType.Date).Value = hasta.HasValue ? hasta.Value.Date : DBNull.Value;
            comando.Parameters.Add("@montoBase", System.Data.SqlDbType.Decimal).Value = (object?)montoBase ?? DBNull.Value;
            comando.Parameters["@montoBase"].Precision = 18;
            comando.Parameters["@montoBase"].Scale = 2;
        }

        public int ContarCuotasConPagos(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            string query = $@"
                SELECT COUNT(*) FROM (
                    SELECT DISTINCT p.FK_id_jugador, YEAR(p.fecha_vencimiento) AS anio, MONTH(p.fecha_vencimiento) AS mes
                    FROM PAGOS p WITH (UPDLOCK, HOLDLOCK)
                    JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador
                    JOIN PERSONA pe ON pe.PK_id_persona = j.FK_id_persona
                    WHERE {FiltroCuotasDelArancel}
                      AND p.estado = 1
                      AND (@montoBase IS NULL OR {CuotaEmitidaConMonto})
                ) cuotas;";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            CargarParametrosCuotas(comando, genero, idCategoria, desde, hasta, montoBase);
            return Convert.ToInt32(comando.ExecuteScalar());
        }

        public int EliminarCuotasPendientesSinPagos(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, DateTime desde, DateTime? hasta, decimal? montoBase)
        {
            // Solo la fila pendiente (estado = 0) de una cuota que no tiene ninguna fila abonada: nunca se
            // borra algo que ya recibió un pago. TR_AUDIT_PAGOS deja registrado cada borrado.
            string query = $@"
                DELETE p
                FROM PAGOS p WITH (UPDLOCK, HOLDLOCK)
                JOIN JUGADORES j ON j.PK_id_jugador = p.FK_id_jugador
                JOIN PERSONA pe ON pe.PK_id_persona = j.FK_id_persona
                WHERE {FiltroCuotasDelArancel}
                  AND p.estado = 0
                  AND (@montoBase IS NULL OR p.monto_base = @montoBase)
                  AND NOT {CuotaTienePagos};";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            CargarParametrosCuotas(comando, genero, idCategoria, desde, hasta, montoBase);
            return comando.ExecuteNonQuery();
        }

        // Genera la cuota de UN mes puntual para los jugadores que todavía no la tienen. Cada
        // jugador paga el arancel que le corresponde por jerarquía: el de su categoría si tiene
        // uno vigente (el género no importa) y, si no, el de su género.
        //
        // El alcance se acota con los filtros: un arancel de género solo toca a los jugadores de
        // ese género, uno de categoría solo a los de esa categoría; sin filtros (generación
        // mensual automática) alcanza a todos. Se puede llamar repetidas veces sin duplicar nada
        // (por el NOT EXISTS), y un jugador sin ningún arancel aplicable simplemente no recibe cuota.
        public void GenerarCuotasPendientesDelMes(string? genero, int? idCategoria, int mes, int anio)
        {
            // Autocontenido (abre su propia transacción): se llama desde el generador mensual,
            // no desde un flujo que ya tenga una transacción abierta.
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            using SqlTransaction transaccion = conexion.BeginTransaction();
            BloqueoEscrituraPagos.Tomar(conexion, transaccion);

            try
            {
                GenerarCuotasPendientesDelMes(conexion, transaccion, genero, idCategoria, mes, anio);
                transaccion.Commit();
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }
        }

        public void GenerarCuotasPendientesDelMes(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int mes, int anio)
            => GenerarCuotas(conexion, transaccion, genero, idCategoria, null, mes, anio);

        // La cuota de UN jugador para un período, con el arancel que le corresponde, si todavía no tiene
        // ninguna fila de cuota en ese mes. Devuelve false si no se emitió: el jugador no existe, ya la
        // tiene, no estaba en el club ese mes (antes del alta o después de la baja) o no hay arancel.
        public bool EmitirCuotaDeJugador(SqlConnection conexion, SqlTransaction transaccion, int idJugador, int mes, int anio)
            => GenerarCuotas(conexion, transaccion, null, null, idJugador, mes, anio) > 0;

        // Un jugador recibe la cuota de un mes solo si estuvo en el club algún día de ese mes: dado de alta
        // antes de que termine (fecha_alta) y sin baja anterior a que empiece (fecha_baja). Sin esto, un
        // arancel cargado para un mes pasado le generaba deuda a quien todavía no había entrado al club, y
        // quien se fue seguía acumulando cuotas para siempre.
        private int GenerarCuotas(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int? idJugador, int mes, int anio)
        {
            // A propósito NO se aplica ningún beneficio de Becados y Descuentos acá: monto_base
            // y monto_final quedan siempre con el arancel CRUDO. El beneficio (si tiene uno,
            // vigente o asignado después) se resuelve dinámicamente cada vez que se lee/cobra
            // la cuota (ver DescuentosSql + PagosDao.ObtenerDeudaDetalle/ObtenerDescuentoAplicableEnPeriodo).
            // Aplicarlo acá también terminaba duplicándolo: una cuota generada con un % ya vigente
            // quedaba con monto_final pre-descontado, y el ajuste dinámico de lectura lo volvía
            // a descontar encima.
            const string query = @"
                -- PK_id_pago sale de la secuencia (default de la columna), uno por fila.
                INSERT INTO PAGOS (FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado)
                SELECT j.PK_id_jugador,
                    arancel.monto, NULL, arancel.monto, NULL, NULL, @primerDiaMes, 0
                FROM JUGADORES j
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                CROSS APPLY (
                    -- Un arancel cargado el 5 de enero cubre igual TODO enero, no solo desde el
                    -- día 5: por eso se compara contra el último día del mes (EOMONTH), no el día 1.
                    -- Primero el arancel de la categoría del jugador; si no tiene, el de su género.
                    SELECT TOP (1) a.monto FROM ARANCELES a
                    WHERE a.vigente_desde <= EOMONTH(@primerDiaMes)
                      AND (a.FK_id_categoria = j.FK_id_categoria
                           OR (a.FK_id_categoria IS NULL AND a.genero = LTRIM(RTRIM(p.genero))))
                    ORDER BY CASE WHEN a.FK_id_categoria IS NULL THEN 1 ELSE 0 END, a.vigente_desde DESC
                ) AS arancel
                WHERE (@genero IS NULL OR LTRIM(RTRIM(p.genero)) = @genero)
                  AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)
                  AND (@idJugador IS NULL OR j.PK_id_jugador = @idJugador)
                  AND (j.fecha_alta IS NULL OR j.fecha_alta <= EOMONTH(@primerDiaMes))
                  AND (j.fecha_baja IS NULL OR j.fecha_baja >= @primerDiaMes)
                  -- Solo cuotas (HU-033): una inscripción en ese mes no reemplaza la cuota.
                  AND NOT EXISTS (
                    SELECT 1 FROM PAGOS p2 WITH (UPDLOCK, HOLDLOCK)
                    WHERE p2.FK_id_jugador = j.PK_id_jugador AND p2.concepto = 'Cuota'
                      AND p2.fecha_vencimiento IS NOT NULL
                      AND MONTH(p2.fecha_vencimiento) = @mes AND YEAR(p2.fecha_vencimiento) = @anio
                );";
            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@genero", System.Data.SqlDbType.VarChar, 20).Value = (object?)genero ?? DBNull.Value;
            comando.Parameters.Add("@idCategoria", System.Data.SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
            comando.Parameters.Add("@idJugador", System.Data.SqlDbType.Int).Value = (object?)idJugador ?? DBNull.Value;
            comando.Parameters.Add("@primerDiaMes", System.Data.SqlDbType.Date).Value = new DateTime(anio, mes, 1);
            comando.Parameters.AddWithValue("@mes", mes);
            comando.Parameters.AddWithValue("@anio", anio);
            return comando.ExecuteNonQuery();
        }

        // El id sale de la secuencia dbo.SEQ_PAGOS (migración V20261011_04): único aunque dos cobros
        // corran a la vez, sin bloquear la tabla. Se pide antes del INSERT para poder devolverlo.
        public int InsertarPago(SqlConnection conexion, SqlTransaction transaccion, Pago pago)
        {
            string queryId = "SELECT NEXT VALUE FOR dbo.SEQ_PAGOS";
            int nuevoId;
            using (SqlCommand comandoId = new SqlCommand(queryId, conexion, transaccion))
            {
                nuevoId = (int)comandoId.ExecuteScalar();
            }

            // fecha_hora_registro = SqlReloj.Ahora (no un parámetro): el momento real en que la fila
            // se graba, tomado del reloj del servidor de base de datos, no del app server.
            string queryInsert = $@"
                INSERT INTO PAGOS (PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, fecha_hora_registro, FK_id_usuario_registro, concepto)
                VALUES (@id, @idJugador, @montoBase, @idJugadorDescuento, @montoFinal, @fechaPago, @metodoPago, @fechaVencimiento, @estado,
                    CASE WHEN @estado = 1 THEN {SqlReloj.Ahora} END, @idUsuarioRegistro, @concepto)";

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
            comando.Parameters.AddWithValue("@idUsuarioRegistro", (object?)pago.IdUsuarioRegistro ?? DBNull.Value);
            comando.Parameters.Add("@concepto", System.Data.SqlDbType.VarChar, 20).Value = pago.Concepto ?? "Cuota";

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
                SELECT PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto
                FROM PAGOS WITH (UPDLOCK, HOLDLOCK)
                WHERE PK_id_pago IN ({clausulaIn})";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddRange(parametros.ToArray());

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                var pago = LeerPago(reader);
                pago.Concepto = reader["concepto"] == DBNull.Value ? null : reader["concepto"].ToString();
                resultado.Add(pago);
            }

            return resultado;
        }

        public bool ExisteJugador(SqlConnection conexion, SqlTransaction transaccion, int idJugador)
        {
            using SqlCommand comando = new SqlCommand(
                "SELECT CASE WHEN EXISTS (SELECT 1 FROM JUGADORES WHERE PK_id_jugador = @idJugador) THEN 1 ELSE 0 END",
                conexion, transaccion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            return Convert.ToInt32(comando.ExecuteScalar()) == 1;
        }

        // HU-025: el WHERE repite las condiciones de negocio (jugador, concepto, estado = 0) aunque
        // PagosService ya las validó sobre filas bloqueadas: si por cualquier motivo una fila no
        // las cumple, no se actualiza, la cantidad no coincide y la excepción revierte TODO el lote.
        // La hora de registro se toma una sola vez del reloj de SQL Server, igual para todas las
        // cuotas del mismo cobro. OUTPUT INTO (no OUTPUT a secas) porque PAGOS tiene trigger.
        public DateTime MarcarPagosComoAbonados(SqlConnection conexion, SqlTransaction transaccion, int idJugador, IEnumerable<int> idsPago, DateTime fechaPago, string metodoPago, int idUsuarioRegistro)
        {
            var ids = idsPago.ToList();
            if (ids.Count == 0)
            {
                throw new InvalidOperationException("No se indicaron cuotas para marcar como abonadas.");
            }

            var (clausulaIn, parametros) = ConstruirClausulaIn("id", ids);
            string query = $@"
                DECLARE @registro DATETIME2 = {SqlReloj.Ahora};
                DECLARE @abonados TABLE (id INT NOT NULL);

                UPDATE PAGOS
                SET estado = 1, fecha_pago = @fechaPago, metodo_pago = @metodoPago,
                    fecha_hora_registro = @registro, FK_id_usuario_registro = @idUsuario
                OUTPUT inserted.PK_id_pago INTO @abonados (id)
                WHERE PK_id_pago IN ({clausulaIn})
                  AND FK_id_jugador = @idJugador AND concepto = 'Cuota' AND estado = 0;

                SELECT COUNT(*) AS filas, @registro AS registro FROM @abonados;";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.AddWithValue("@fechaPago", fechaPago);
            comando.Parameters.AddWithValue("@metodoPago", metodoPago);
            comando.Parameters.AddWithValue("@idUsuario", idUsuarioRegistro);
            comando.Parameters.AddWithValue("@idJugador", idJugador);
            comando.Parameters.AddRange(parametros.ToArray());

            using SqlDataReader reader = comando.ExecuteReader();
            reader.Read();
            int filasAfectadas = Convert.ToInt32(reader["filas"]);
            if (filasAfectadas != ids.Count)
            {
                throw new InvalidOperationException(
                    $"Se esperaba actualizar {ids.Count} pago(s) y se actualizaron {filasAfectadas}.");
            }

            return Convert.ToDateTime(reader["registro"]);
        }

        public void ActualizarMontoCobroConDescuento(SqlConnection conexion, SqlTransaction transaccion, int idPago, int idDescuento, decimal montoFinal)
        {
            using var comando = new SqlCommand(@"UPDATE PAGOS SET FK_id_jugador_descuento = @descuento, monto_final = @monto
                WHERE PK_id_pago = @id AND estado = 0 AND concepto = 'Cuota'", conexion, transaccion);
            comando.Parameters.AddWithValue("@descuento", idDescuento);
            comando.Parameters.AddWithValue("@monto", montoFinal);
            comando.Parameters.AddWithValue("@id", idPago);
            if (comando.ExecuteNonQuery() != 1) throw new InvalidOperationException("No se pudo actualizar el monto de la cuota.");
        }

        private const string ColumnasPagoCompleto = @"PK_id_pago, FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final,
                fecha_pago, metodo_pago, fecha_vencimiento, estado, concepto, fecha_hora_registro, FK_id_usuario_registro";

        public Pago? ObtenerPagoParaAnular(SqlConnection conexion, SqlTransaction transaccion, int idPago)
        {
            using SqlCommand comando = new SqlCommand(
                $"SELECT {ColumnasPagoCompleto} FROM PAGOS WITH (UPDLOCK, HOLDLOCK) WHERE PK_id_pago = @id",
                conexion, transaccion);
            comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = idPago;

            using SqlDataReader reader = comando.ExecuteReader();
            return reader.Read() ? LeerPagoCompleto(reader) : null;
        }

        public IReadOnlyList<Pago> ObtenerFilasDeLaMismaDeuda(SqlConnection conexion, SqlTransaction transaccion, Pago pago)
        {
            // Inscripción: una sola por jugador. Cuota: el mismo mes de fecha_vencimiento (el período).
            string query = $@"
                SELECT {ColumnasPagoCompleto}
                FROM PAGOS WITH (UPDLOCK, HOLDLOCK)
                WHERE FK_id_jugador = @idJugador AND concepto = @concepto
                  AND (@concepto = 'Inscripcion'
                       OR (fecha_vencimiento >= @inicio AND fecha_vencimiento < @fin))";

            DateTime inicio = pago.FechaVencimiento.HasValue
                ? new DateTime(pago.FechaVencimiento.Value.Year, pago.FechaVencimiento.Value.Month, 1)
                : DateTime.MinValue;

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@idJugador", System.Data.SqlDbType.Int).Value = pago.IdJugador;
            comando.Parameters.Add("@concepto", System.Data.SqlDbType.VarChar, 20).Value = pago.Concepto ?? "Cuota";
            comando.Parameters.Add("@inicio", System.Data.SqlDbType.Date).Value = pago.FechaVencimiento.HasValue ? inicio : DBNull.Value;
            comando.Parameters.Add("@fin", System.Data.SqlDbType.Date).Value = pago.FechaVencimiento.HasValue ? inicio.AddMonths(1) : DBNull.Value;

            var resultado = new List<Pago>();
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(LeerPagoCompleto(reader));
            }
            return resultado;
        }

        public void RegistrarAnulacionYEliminarAbono(SqlConnection conexion, SqlTransaction transaccion, Pago abono, string motivo, int idUsuarioAnulacion)
        {
            // El DELETE exige estado = 1 para no borrar nunca una cuota pendiente por esta vía. OUTPUT INTO
            // (no OUTPUT a secas ni @@ROWCOUNT) porque PAGOS tiene trigger de auditoría.
            string query = $@"
                DECLARE @borrados TABLE (id INT NOT NULL);

                INSERT INTO dbo.PAGOS_ANULADOS (id_pago, FK_id_jugador, concepto, monto, monto_base, metodo_pago, fecha_pago,
                    fecha_vencimiento, fecha_hora_registro_pago, FK_id_usuario_registro_pago, motivo, FK_id_usuario_anulacion, fecha_hora_anulacion)
                SELECT PK_id_pago, FK_id_jugador, concepto, monto_final, monto_base, metodo_pago, fecha_pago,
                    fecha_vencimiento, fecha_hora_registro, FK_id_usuario_registro, @motivo, @idUsuario, {SqlReloj.Ahora}
                FROM PAGOS WHERE PK_id_pago = @id AND estado = 1;

                DELETE FROM PAGOS OUTPUT deleted.PK_id_pago INTO @borrados (id) WHERE PK_id_pago = @id AND estado = 1;
                SELECT COUNT(*) FROM @borrados;";

            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@id", System.Data.SqlDbType.Int).Value = abono.IdPago;
            comando.Parameters.Add("@motivo", System.Data.SqlDbType.NVarChar, 500).Value = motivo;
            comando.Parameters.Add("@idUsuario", System.Data.SqlDbType.Int).Value = idUsuarioAnulacion;

            if (Convert.ToInt32(comando.ExecuteScalar()) != 1)
            {
                throw new InvalidOperationException($"El pago #{abono.IdPago} cambió mientras se anulaba.");
            }
        }

        // Como LeerPago, pero tolera monto_base NULL (pagos históricos) y trae concepto y trazabilidad.
        private static Pago LeerPagoCompleto(SqlDataReader reader)
        {
            decimal montoFinal = reader["monto_final"] != DBNull.Value ? Convert.ToDecimal(reader["monto_final"]) : 0m;
            return new Pago
            {
                IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                IdJugador = Convert.ToInt32(reader["FK_id_jugador"]),
                MontoBase = reader["monto_base"] != DBNull.Value ? Convert.ToDecimal(reader["monto_base"]) : montoFinal,
                IdJugadorDescuento = reader["FK_id_jugador_descuento"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_jugador_descuento"]) : null,
                MontoFinal = montoFinal,
                FechaPago = reader["fecha_pago"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_pago"]) : null,
                MetodoPago = reader["metodo_pago"] != DBNull.Value ? reader["metodo_pago"].ToString() : null,
                FechaVencimiento = reader["fecha_vencimiento"] != DBNull.Value ? Convert.ToDateTime(reader["fecha_vencimiento"]) : null,
                Estado = reader["estado"] != DBNull.Value && Convert.ToBoolean(reader["estado"]),
                Concepto = reader["concepto"].ToString()?.Trim(),
                IdUsuarioRegistro = reader["FK_id_usuario_registro"] != DBNull.Value ? Convert.ToInt32(reader["FK_id_usuario_registro"]) : null
            };
        }

        // HU-020: idCategoria es opcional — null trae el padrón completo (comportamiento previo),
        // con un valor acota el resultado a esa división. El filtro se resuelve en el propio WHERE
        // (no en un HAVING aparte) para que el motor pueda usarlo antes de agrupar.
        public IReadOnlyList<PendienteJugador> ObtenerPendientesAgrupados(int? idCategoria = null)
        {
            var resultado = new List<PendienteJugador>();

            // El saldo de cada cuota ya sale con el beneficio de Becados y Descuentos aplicado
            // (si tiene uno vigente para ese período), aunque la cuota se haya cargado antes de
            // asignarle el beneficio. Un jugador cuyo beneficio le deja todo en $0 desaparece de
            // este panel (HAVING > 0): no tiene nada pendiente de cobro de verdad.
            //
            // HU-033: monto_total suma cuotas e inscripción; cantidad_cuotas cuenta solo cuotas,
            // porque de ella sale el estado deportivo (Solo entrenamientos / Inhabilitado).
            string query = $@"
                SELECT j.PK_id_jugador, j.FK_id_categoria, p.nombre, p.apellido, p.Dni, c.nombre_categoria,
                       SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}) AS monto_total,
                       COUNT(CASE WHEN ({DescuentosSql.SaldoAjustadoExpr}) > 0 AND pg.concepto = 'Cuota' THEN 1 END) AS cantidad_cuotas
                FROM PAGOS pg
                {DescuentosSql.ApplyDescuentoActivo}
                JOIN JUGADORES j ON pg.FK_id_jugador = j.PK_id_jugador
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                WHERE pg.estado = 0 AND pg.concepto = 'Cuota'
                  AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)
                GROUP BY j.PK_id_jugador, j.FK_id_categoria, p.nombre, p.apellido, p.Dni, c.nombre_categoria
                HAVING SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}) > 0
                -- HU-021 (QA, 24/09): el reporte exportado tiene que salir en el mismo orden que la
                -- grilla de Deudas y Morosidad (cantidad de cuotas primero, monto como desempate).
                ORDER BY cantidad_cuotas DESC, monto_total DESC";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@idCategoria", (object?)idCategoria ?? DBNull.Value);

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new PendienteJugador
                {
                    IdJugador = Convert.ToInt32(reader["PK_id_jugador"]),
                    NombreCompleto = $"{reader["apellido"].ToString()?.Trim()}, {reader["nombre"].ToString()?.Trim()}",
                    Dni = reader["Dni"].ToString()?.Trim() ?? "",
                    IdCategoria = Convert.ToInt32(reader["FK_id_categoria"]),
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    MontoTotal = Convert.ToDecimal(reader["monto_total"]),
                    CantidadCuotas = Convert.ToInt32(reader["cantidad_cuotas"])
                });
            }

            return resultado;
        }

        // HU-029: una fila por jugador con lo que debe. La deuda se agrupa en una sola pasada sobre
        // PAGOS (sin una consulta por jugador) con la misma fórmula que ObtenerPendientesAgrupados
        // y que la "Deuda Global Total", así que los montos suman ese indicador y los deudores
        // listados son exactamente los "jugadores morosos" que cuenta.
        //
        // Sin onlyDebtors vuelve el padrón completo (debe 0 si está al día). Con onlyDebtors el
        // WHERE deja solo a quien tiene saldo mayor a cero: un jugador cuyo beneficio deja todas
        // sus cuotas en $0 no es deudor. El índice de cuotas pendientes (IX_PAGOS_Estado_Pendientes)
        // cubre la lectura de PAGOS.
        public IReadOnlyList<PlayerAccount> GetPlayerAccounts(bool onlyDebtors)
        {
            var accounts = new List<PlayerAccount>();

            string filter = onlyDebtors ? "WHERE d.monto_adeudado > 0" : "";
            string order = onlyDebtors
                ? "ORDER BY d.monto_adeudado DESC, p.apellido, p.nombre"
                : "ORDER BY p.apellido, p.nombre";

            string query = $@"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni, c.nombre_categoria,
                       ISNULL(d.monto_adeudado, 0) AS monto_adeudado,
                       ISNULL(d.cantidad_cuotas, 0) AS cantidad_cuotas
                FROM JUGADORES j
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                LEFT JOIN (
                    SELECT t.FK_id_jugador,
                           SUM(t.saldo) AS monto_adeudado,
                           COUNT(CASE WHEN t.saldo > 0 AND t.concepto = 'Cuota' THEN 1 END) AS cantidad_cuotas
                    FROM (
                        SELECT pg.FK_id_jugador, pg.concepto, ({DescuentosSql.SaldoAjustadoClampleadoExpr}) AS saldo
                        FROM PAGOS pg
                        {DescuentosSql.ApplyDescuentoActivo}
                        WHERE pg.estado = 0
                    ) t
                    GROUP BY t.FK_id_jugador
                ) d ON d.FK_id_jugador = j.PK_id_jugador
                {filter}
                {order}";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                accounts.Add(new PlayerAccount
                {
                    PlayerId = Convert.ToInt32(reader["PK_id_jugador"]),
                    FirstName = reader["nombre"].ToString()?.Trim() ?? "",
                    LastName = reader["apellido"].ToString()?.Trim() ?? "",
                    Dni = reader["Dni"].ToString()?.Trim() ?? "",
                    Category = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    AmountOwed = Convert.ToDecimal(reader["monto_adeudado"]),
                    PendingInstallments = Convert.ToInt32(reader["cantidad_cuotas"])
                });
            }

            return accounts;
        }

        public IReadOnlyList<CategoriaDeuda> ObtenerDeudaPorCategoria(int anio, int? mes = null)
        {
            var resultado = new List<CategoriaDeuda>();

            // No hace falta JOIN a PERSONA acá (no se necesita nombre/DNI, solo el total por
            // categoría), así que la consulta queda liviana aunque el club tenga 700+ jugadores.
            string query = $@"
                SELECT j.FK_id_categoria, c.nombre_categoria,
                       SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}) AS monto_total,
                       COUNT(DISTINCT CASE WHEN ({DescuentosSql.SaldoAjustadoExpr}) > 0 THEN j.PK_id_jugador END) AS cantidad_jugadores
                FROM PAGOS pg
                {DescuentosSql.ApplyDescuentoActivo}
                JOIN JUGADORES j ON pg.FK_id_jugador = j.PK_id_jugador
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                WHERE pg.estado = 0
                  AND pg.fecha_vencimiento IS NOT NULL
                  AND YEAR(pg.fecha_vencimiento) = @anio
                  AND (@mes IS NULL OR MONTH(pg.fecha_vencimiento) = @mes)
                GROUP BY j.FK_id_categoria, c.nombre_categoria
                HAVING SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}) > 0
                ORDER BY monto_total DESC";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@anio", anio);
            comando.Parameters.AddWithValue("@mes", (object?)mes ?? DBNull.Value);

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                resultado.Add(new CategoriaDeuda
                {
                    IdCategoria = Convert.ToInt32(reader["FK_id_categoria"]),
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    MontoTotal = Convert.ToDecimal(reader["monto_total"]),
                    CantidadJugadores = Convert.ToInt32(reader["cantidad_jugadores"])
                });
            }

            return resultado;
        }

        public IReadOnlyList<PagoReciente> ObtenerUltimosPagos(int top) => ObtenerPagosAgrupados($"TOP ({Math.Clamp(top, 1, 1000)})");

        // Sin límite: la tabla "Pagos registrados" de Actividad y Movimientos trae todo y
        // filtra/pagina del lado del cliente, mismo criterio que Becados y Descuentos.
        public IReadOnlyList<PagoReciente> ObtenerTodosLosPagos() => ObtenerPagosAgrupados("TOP (1000)");

        // Si una cuota se terminó de pagar (o se viene pagando) en VARIOS abonos (ej. $20.000 +
        // $50.000), acá tiene que verse como UN solo renglón de $70.000 — no dos líneas
        // separadas. Por eso se agrupa por jugador+período: se suman los montos, se toma la
        // fecha/hora del abono más reciente, y su método de pago representa al conjunto.
        //
        // es_parcial distingue las dos situaciones sin filtrar ninguna: si todavía queda una
        // cuota pendiente (estado = 0) de ese mismo jugador+período, lo abonado hasta ahora es
        // un abono PARCIAL (la cuota sigue abierta); si no queda ninguna, ese grupo terminó de
        // cubrir el total y es un pago COMPLETO. Antes esta consulta solo traía los completos.
        //
        // topClause: "TOP (n)" (el n ya viene validado como int por el controller, no hace
        // falta parametrizarlo) o null para traer todos.
        private IReadOnlyList<PagoReciente> ObtenerPagosAgrupados(string? topClause)
        {
            var resultado = new List<PagoReciente>();

            // HU-033: el grupo es jugador + período + concepto. Una inscripción pendiente del mes
            // del alta no esconde la cuota ya pagada ni suma abonos juntos.
            // Cuotas: solo aparecen cuando están totalmente pagas (sin fila pendiente del mismo
            // período+concepto). Inscripciones: aparecen siempre, incluso con saldo pendiente.
            string query = $@"
                ;WITH Grupos AS (
                    SELECT pg.PK_id_pago, pg.FK_id_jugador, pg.metodo_pago, pg.fecha_vencimiento, pg.concepto,
                        SUM(pg.monto_final) OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento, pg.concepto) AS monto_grupo,
                        MAX(pg.fecha_pago) OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento, pg.concepto) AS fecha_grupo,
                        MAX(pg.fecha_hora_registro) OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento, pg.concepto) AS fecha_hora_grupo,
                        ROW_NUMBER() OVER (PARTITION BY pg.FK_id_jugador, pg.fecha_vencimiento, pg.concepto ORDER BY pg.fecha_pago DESC, pg.PK_id_pago DESC) AS rn,
                        CASE WHEN EXISTS (
                            SELECT 1 FROM PAGOS pendiente
                            WHERE pendiente.FK_id_jugador = pg.FK_id_jugador
                              AND pendiente.concepto = pg.concepto
                              AND pendiente.estado = 0
                              AND pendiente.fecha_vencimiento IS NOT NULL AND pg.fecha_vencimiento IS NOT NULL
                              AND MONTH(pendiente.fecha_vencimiento) = MONTH(pg.fecha_vencimiento)
                              AND YEAR(pendiente.fecha_vencimiento) = YEAR(pg.fecha_vencimiento)
                        ) THEN 1 ELSE 0 END AS es_parcial
                    FROM PAGOS pg
                    WHERE pg.estado = 1 AND pg.fecha_pago IS NOT NULL
                      AND (
                        pg.concepto = 'Inscripcion'
                        OR NOT EXISTS (
                            SELECT 1 FROM PAGOS pendiente
                            WHERE pendiente.FK_id_jugador = pg.FK_id_jugador
                              AND pendiente.concepto = pg.concepto
                              AND pendiente.estado = 0
                              AND pendiente.fecha_vencimiento IS NOT NULL AND pg.fecha_vencimiento IS NOT NULL
                              AND MONTH(pendiente.fecha_vencimiento) = MONTH(pg.fecha_vencimiento)
                              AND YEAR(pendiente.fecha_vencimiento) = YEAR(pg.fecha_vencimiento)
                        )
                      )
                )
                SELECT {(topClause != null ? topClause + " " : "")}g.PK_id_pago, g.FK_id_jugador, p.nombre, p.apellido, c.nombre_categoria,
                    g.metodo_pago, g.monto_grupo AS monto_final, g.fecha_grupo AS fecha_pago,
                    g.fecha_hora_grupo AS fecha_hora_registro, g.es_parcial, g.fecha_vencimiento, g.concepto,
                    responsable.nombre AS responsable_nombre, responsable.apellido AS responsable_apellido,
                    responsable.fecha_utc AS responsable_fecha_utc
                FROM Grupos g
                OUTER APPLY (
                    SELECT TOP (1) ac.nombre_usuario AS nombre, ac.apellido_usuario AS apellido, ac.fecha_utc
                    FROM dbo.AUDITORIA_CAMBIOS ac
                    WHERE ac.entidad = N'PAGOS' AND ac.id_entidad = CONVERT(NVARCHAR(128), g.PK_id_pago)
                      AND (
                          (ac.accion = 'INSERT' AND LOWER(JSON_VALUE(ac.datos_despues, '$.estado')) IN ('1', 'true'))
                          OR (ac.accion = 'UPDATE'
                              AND LOWER(JSON_VALUE(ac.datos_antes, '$.estado')) IN ('0', 'false')
                              AND LOWER(JSON_VALUE(ac.datos_despues, '$.estado')) IN ('1', 'true'))
                      )
                    ORDER BY CASE WHEN ac.accion = 'UPDATE' THEN 0 ELSE 1 END, ac.fecha_utc, ac.PK_id_evento
                ) responsable
                JOIN JUGADORES j ON g.FK_id_jugador = j.PK_id_jugador
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                JOIN CATEGORIAS c ON j.FK_id_categoria = c.PK_id_categoria
                WHERE g.rn = 1
                ORDER BY g.fecha_hora_grupo DESC, g.fecha_grupo DESC, g.PK_id_pago DESC";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                var periodo = Convert.ToDateTime(reader["fecha_vencimiento"]);
                var esParcial = Convert.ToInt32(reader["es_parcial"]) == 1;

                resultado.Add(new PagoReciente
                {
                    IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                    IdJugador = Convert.ToInt32(reader["FK_id_jugador"]),
                    NombreCompleto = $"{reader["apellido"].ToString()?.Trim()}, {reader["nombre"].ToString()?.Trim()}",
                    Categoria = reader["nombre_categoria"].ToString()?.Trim() ?? "",
                    Periodo = $"{MesesCompletos[periodo.Month - 1]} {periodo.Year}",
                    MetodoPago = reader["metodo_pago"].ToString()?.Trim() ?? "",
                    Monto = Convert.ToDecimal(reader["monto_final"]),
                    Estado = esParcial ? "Parcial" : "Pagado",
                    FechaPago = Convert.ToDateTime(reader["fecha_pago"]),
                    Concepto = reader["concepto"].ToString()?.Trim() ?? "Cuota",
                    ResponsableNombre = reader["responsable_nombre"] == DBNull.Value ? null : reader["responsable_nombre"].ToString()?.Trim(),
                    ResponsableApellido = reader["responsable_apellido"] == DBNull.Value ? null : reader["responsable_apellido"].ToString()?.Trim(),
                    FechaHoraRegistro = reader["responsable_fecha_utc"] != DBNull.Value
                        ? Convert.ToDateTime(reader["responsable_fecha_utc"])
                        : reader["fecha_hora_registro"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_hora_registro"])
                });
            }

            return resultado;
        }

        // Métricas del banner de "Actividad y Movimientos": puntuales de HOY, a diferencia de
        // ObtenerResumen (año/mes).
        public ResumenPagosHoy ObtenerResumenHoy()
        {
            string query = $@"
                SELECT
                    (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND CAST(fecha_pago AS DATE) = {SqlReloj.Hoy}) AS pagos_hoy,
                    (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS WHERE estado = 1 AND CAST(fecha_pago AS DATE) = {SqlReloj.Hoy}) AS recaudado_hoy";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            reader.Read();

            return new ResumenPagosHoy
            {
                PagosHoy = Convert.ToInt32(reader["pagos_hoy"]),
                RecaudadoHoy = Convert.ToDecimal(reader["recaudado_hoy"])
            };
        }

        // Detalle de deuda de un jugador: cada cuota todavía pendiente (estado = 0), con lo que
        // ya abonó para ese período (estado = 1, mismo mes/año) y cuánto le sigue faltando.
        public IReadOnlyList<CuotaPendienteDetalle> ObtenerDeudaDetalle(int idJugador)
        {
            var pendientes = new List<CuotaPendienteDetalle>();

            // El saldo pendiente sale ya con el beneficio de Becados y Descuentos aplicado (si el
            // jugador tiene uno activo cuya vigencia cubre el mes de esta cuota) — no importa que
            // la cuota se haya cargado antes de asignarle el beneficio, se resuelve acá al leer.
            string queryPendientes = $@"
                SELECT pg.PK_id_pago, pg.monto_base, pg.monto_final, pg.fecha_vencimiento, pg.concepto,
                       d.tipo_valor, d.porcentaje, d.monto_fijo, td.tipo_descuento AS motivo,
                       ({DescuentosSql.SaldoAjustadoExpr}) AS saldo_ajustado
                FROM PAGOS pg
                {DescuentosSql.ApplyDescuentoActivo}
                LEFT JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = d.FK_id_descuento
                WHERE pg.FK_id_jugador = @idJugador AND pg.estado = 0
                ORDER BY pg.fecha_vencimiento, pg.concepto";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using (SqlCommand comando = new SqlCommand(queryPendientes, conexion))
            {
                comando.Parameters.AddWithValue("@idJugador", idJugador);
                using SqlDataReader reader = comando.ExecuteReader();
                while (reader.Read())
                {
                    var periodoBase = Convert.ToDateTime(reader["fecha_vencimiento"]);
                    var tieneBeneficio = reader["motivo"] != DBNull.Value;
                    var saldoAjustado = Convert.ToDecimal(reader["saldo_ajustado"]);

                    pendientes.Add(new CuotaPendienteDetalle
                    {
                        IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                        Periodo = $"{MesesCompletos[periodoBase.Month - 1]} {periodoBase.Year}",
                        Concepto = reader["concepto"].ToString()?.Trim() ?? "",
                        MontoOriginal = Convert.ToDecimal(reader["monto_base"]),
                        SaldoPendiente = saldoAjustado < 0 ? 0 : saldoAjustado,
                        TieneBeneficio = tieneBeneficio,
                        MotivoBeneficio = tieneBeneficio ? reader["motivo"].ToString()?.Trim() : null,
                        TipoValorBeneficio = tieneBeneficio ? reader["tipo_valor"].ToString()?.Trim() : null,
                        PorcentajeBeneficio = reader["porcentaje"] != DBNull.Value ? Convert.ToDecimal(reader["porcentaje"]) : null,
                        MontoFijoBeneficio = reader["monto_fijo"] != DBNull.Value ? Convert.ToDecimal(reader["monto_fijo"]) : null
                    });
                }
            }

            if (pendientes.Count == 0)
            {
                return pendientes;
            }

            // Todos los abonos (estado = 1) del jugador, para matchear por período (mes/año de
            // fecha_vencimiento) y concepto contra cada cargo pendiente de arriba: la inscripción
            // y la cuota del mes del alta comparten período (HU-033).
            string queryAbonos = @"
                SELECT monto_final, metodo_pago, fecha_pago, fecha_vencimiento, concepto
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
                    var concepto = reader["concepto"].ToString()?.Trim() ?? "";
                    var cuota = pendientes.FirstOrDefault(c =>
                        c.Concepto == concepto
                        && c.Periodo == $"{MesesCompletos[fechaVencimiento.Month - 1]} {fechaVencimiento.Year}");

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

        // HU-025: filas crudas de las cuotas del jugador. Las cuotas históricas sin fecha_vencimiento
        // no se incluyen: sin período no se pueden asociar a una cuota mensual (siguen visibles en
        // el Historial de Pagos). El saldo ajustado usa el mismo criterio que el detalle de deuda.
        public IReadOnlyList<CuotaMovimiento> ObtenerMovimientosCuotas(int idJugador)
        {
            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);
            return LeerMovimientosCuotas(conexion, idJugador);
        }

        // HU-024: estado de cuenta del jugador. Lee sus datos y las filas de sus cuotas en una
        // sola conexión; null si el jugador no existe. Solo concepto 'Cuota': la inscripción se
        // abona por separado y es otro ingreso, así que no forma parte del estado de cuenta.
        // Los montos son los congelados en PAGOS al emitirse cada cuota (monto_base, regla §2.3).
        public PlayerStatementAccount? GetPlayerStatementAccount(int playerId)
        {
            const string queryJugador = @"
                SELECT j.PK_id_jugador, p.nombre, p.apellido, p.Dni
                FROM JUGADORES j
                JOIN PERSONA p ON j.FK_id_persona = p.PK_id_persona
                WHERE j.PK_id_jugador = @idJugador";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            PlayerStatementAccount cuenta;
            using (SqlCommand comando = new SqlCommand(queryJugador, conexion))
            {
                comando.Parameters.Add("@idJugador", System.Data.SqlDbType.Int).Value = playerId;
                using SqlDataReader reader = comando.ExecuteReader();
                if (!reader.Read())
                {
                    return null;
                }

                cuenta = new PlayerStatementAccount
                {
                    PlayerId = Convert.ToInt32(reader["PK_id_jugador"]),
                    FirstName = reader["nombre"].ToString()?.Trim() ?? "",
                    LastName = reader["apellido"].ToString()?.Trim() ?? "",
                    Dni = reader["Dni"].ToString()?.Trim() ?? ""
                };
            }

            cuenta.Movements = LeerMovimientosCuotas(conexion, playerId);
            return cuenta;
        }

        private static IReadOnlyList<CuotaMovimiento> LeerMovimientosCuotas(SqlConnection conexion, int idJugador)
        {
            var movimientos = new List<CuotaMovimiento>();

            string query = $@"
                SELECT pg.PK_id_pago, pg.estado, pg.monto_base, pg.monto_final, pg.fecha_vencimiento,
                       pg.fecha_pago, pg.metodo_pago, td.tipo_descuento AS motivo,
                       ({DescuentosSql.SaldoAjustadoExpr}) AS saldo_ajustado
                FROM PAGOS pg
                {DescuentosSql.ApplyDescuentoActivo}
                LEFT JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = d.FK_id_descuento
                WHERE pg.FK_id_jugador = @idJugador AND pg.concepto = 'Cuota' AND pg.fecha_vencimiento IS NOT NULL
                ORDER BY pg.fecha_vencimiento, pg.fecha_pago, pg.PK_id_pago";

            using SqlCommand comando = new SqlCommand(query, conexion);
            comando.Parameters.AddWithValue("@idJugador", idJugador);

            using SqlDataReader reader = comando.ExecuteReader();
            while (reader.Read())
            {
                decimal montoFinal = reader["monto_final"] == DBNull.Value ? 0 : Convert.ToDecimal(reader["monto_final"]);
                movimientos.Add(new CuotaMovimiento
                {
                    IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                    Estado = reader["estado"] != DBNull.Value && Convert.ToBoolean(reader["estado"]),
                    MontoBase = reader["monto_base"] == DBNull.Value ? montoFinal : Convert.ToDecimal(reader["monto_base"]),
                    MontoFinal = montoFinal,
                    SaldoAjustado = reader["saldo_ajustado"] == DBNull.Value ? montoFinal : Convert.ToDecimal(reader["saldo_ajustado"]),
                    FechaVencimiento = Convert.ToDateTime(reader["fecha_vencimiento"]),
                    FechaPago = reader["fecha_pago"] == DBNull.Value ? null : Convert.ToDateTime(reader["fecha_pago"]),
                    MetodoPago = reader["metodo_pago"] == DBNull.Value ? null : reader["metodo_pago"].ToString()?.Trim(),
                    MotivoBeneficio = reader["motivo"] == DBNull.Value ? null : reader["motivo"].ToString()?.Trim()
                });
            }

            return movimientos;
        }

        public ResumenPagos ObtenerResumen()
        {
            // cantidad_pendientes solo cuenta cuotas con saldo real > 0: una cuota que un
            // beneficio de Becados y Descuentos dejó en $0 ya no es algo pendiente de cobrar.
            // Las inscripciones (HU-033) no son cuotas: suman a la deuda pero no a este conteo.
            //
            // deuda_global_total / jugadores_morosos (HU-019): mismo criterio de saldo real que
            // cantidad_pendientes, pero uno suma $ (por jugador, no por cuota) y el otro cuenta
            // jugadores distintos en vez de cuotas. A propósito NO se multiplica "cantidad de
            // cuotas x arancel vigente hoy": eso violaría la regla de negocio del arancel
            // congelado (project.md §2.3) y daría mal en cuanto dos categorías tengan aranceles
            // distintos (ej. Femenino Primera vs Sub17). Se suma el saldo real de cada cuota, ya
            // congelado al mes en que se emitió.
            string query = $@"
                SELECT
                    (SELECT ISNULL(SUM(monto_final), 0) FROM PAGOS WHERE estado = 1 AND YEAR(fecha_pago) = YEAR({SqlReloj.Ahora})) AS recaudado_anio,
                    (SELECT COUNT(*) FROM PAGOS WHERE estado = 1 AND YEAR(fecha_pago) = YEAR({SqlReloj.Ahora}) AND MONTH(fecha_pago) = MONTH({SqlReloj.Ahora})) AS pagos_del_mes,
                    (SELECT COUNT(*) FROM (
                        SELECT ({DescuentosSql.SaldoAjustadoExpr}) AS saldo_ajustado
                        FROM PAGOS pg
                        {DescuentosSql.ApplyDescuentoActivo}
                        WHERE pg.estado = 0 AND pg.concepto = 'Cuota'
                    ) t WHERE saldo_ajustado > 0) AS cantidad_pendientes,
                    (SELECT ISNULL(SUM({DescuentosSql.SaldoAjustadoClampleadoExpr}), 0)
                        FROM PAGOS pg
                        {DescuentosSql.ApplyDescuentoActivo}
                        WHERE pg.estado = 0) AS deuda_global_total,
                    (SELECT COUNT(DISTINCT t.FK_id_jugador) FROM (
                        SELECT pg.FK_id_jugador, ({DescuentosSql.SaldoAjustadoExpr}) AS saldo_ajustado
                        FROM PAGOS pg
                        {DescuentosSql.ApplyDescuentoActivo}
                        WHERE pg.estado = 0
                    ) t WHERE t.saldo_ajustado > 0) AS jugadores_morosos";

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

            using SqlCommand comando = new SqlCommand(query, conexion);
            using SqlDataReader reader = comando.ExecuteReader();
            reader.Read();

            return new ResumenPagos
            {
                RecaudadoAnioActual = Convert.ToDecimal(reader["recaudado_anio"]),
                PagosDelMes = Convert.ToInt32(reader["pagos_del_mes"]),
                CantidadPendientes = Convert.ToInt32(reader["cantidad_pendientes"]),
                DeudaGlobalTotal = Convert.ToDecimal(reader["deuda_global_total"]),
                JugadoresMorosos = Convert.ToInt32(reader["jugadores_morosos"])
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

            // monto_base_grupo: el valor ORIGINAL de la cuota (PagosService ahora lo guarda en cada
            // abono cuando paga contra una cuota pendiente — antes se perdía al borrarse la cuota).
            // id_descuento_grupo: qué beneficio (si hubo uno) se le aplicó a esos abonos, para
            // mostrar el motivo/tipo/valor y explicar por qué monto_grupo < monto_base_grupo.
            //
            // HU-033: cada grupo es período + concepto, así la inscripción no se suma a la cuota
            // del mes del alta (comparten fecha de período).
            string queryPeriodos = @"
                ;WITH Agrupado AS (
                    SELECT PK_id_pago, fecha_vencimiento, concepto,
                        COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)) AS clave_periodo,
                        SUM(monto_final) OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)), concepto) AS monto_grupo,
                        MAX(monto_base) OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)), concepto) AS monto_base_grupo,
                        MAX(FK_id_jugador_descuento) OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)), concepto) AS id_descuento_grupo,
                        MAX(fecha_pago) OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)), concepto) AS fecha_grupo,
                        ROW_NUMBER() OVER (PARTITION BY COALESCE(fecha_vencimiento, CAST(fecha_pago AS DATE)), concepto ORDER BY fecha_pago DESC, PK_id_pago DESC) AS rn
                    FROM PAGOS
                    WHERE FK_id_jugador = @idJugador AND estado = 1
                )
                SELECT COUNT(*) OVER() AS total_count, a.clave_periodo, a.concepto, a.monto_grupo, a.monto_base_grupo,
                       a.fecha_grupo, a.fecha_vencimiento, jd.tipo_valor, jd.porcentaje, jd.monto_fijo, td.tipo_descuento AS motivo
                FROM Agrupado a
                LEFT JOIN JUGADORES_DESCUENTOS jd ON jd.PK_id_jugador_descuento = a.id_descuento_grupo
                LEFT JOIN TIPO_DESCUENTO td ON td.PK_id_descuento = jd.FK_id_descuento
                WHERE a.rn = 1
                ORDER BY a.fecha_grupo DESC, a.clave_periodo DESC, a.concepto
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY";

            // (clave_periodo, concepto, PagoHistorialItem) en el orden en que vinieron, para después
            // completar cada uno con sus abonos sin perder el orden de fecha_grupo DESC.
            var itemsPorClave = new List<(DateTime clave, string concepto, PagoHistorialItem item)>();

            using SqlConnection conexion = SqlConnectionFactory.Open(_cadenaConexion);

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

                    var tieneBeneficio = reader["motivo"] != DBNull.Value;
                    var concepto = reader["concepto"].ToString()?.Trim() ?? "";

                    var item = new PagoHistorialItem
                    {
                        Periodo = $"{MesesCompletos[periodoBase.Month - 1]} {periodoBase.Year}",
                        Concepto = concepto,
                        MontoTotal = Convert.ToDecimal(reader["monto_grupo"]),
                        MontoOriginal = Convert.ToDecimal(reader["monto_base_grupo"]),
                        TieneBeneficio = tieneBeneficio,
                        MotivoBeneficio = tieneBeneficio ? reader["motivo"].ToString()?.Trim() : null,
                        TipoValorBeneficio = tieneBeneficio ? reader["tipo_valor"].ToString()?.Trim() : null,
                        PorcentajeBeneficio = reader["porcentaje"] != DBNull.Value ? Convert.ToDecimal(reader["porcentaje"]) : null,
                        MontoFijoBeneficio = reader["monto_fijo"] != DBNull.Value ? Convert.ToDecimal(reader["monto_fijo"]) : null
                    };
                    itemsPorClave.Add((clave, concepto, item));
                    resultado.Items.Add(item);
                }
            }

            // Los abonos de cada período de esta página, todos de una.
            foreach (var (clave, concepto, item) in itemsPorClave)
            {
                string queryAbonos = @"
                SELECT pg.PK_id_pago, pg.monto_final, pg.metodo_pago, pg.fecha_pago, pg.fecha_hora_registro,
                       responsable.nombre AS responsable_nombre, responsable.apellido AS responsable_apellido,
                       responsable.fecha_utc AS auditoria_fecha_utc
                    FROM PAGOS pg
                    OUTER APPLY (
                        SELECT TOP (1) ac.nombre_usuario AS nombre, ac.apellido_usuario AS apellido, ac.fecha_utc
                        FROM dbo.AUDITORIA_CAMBIOS ac
                        WHERE ac.entidad = N'PAGOS' AND ac.id_entidad = CONVERT(NVARCHAR(128), pg.PK_id_pago)
                          AND (
                              (ac.accion = 'INSERT' AND LOWER(JSON_VALUE(ac.datos_despues, '$.estado')) IN ('1', 'true'))
                              OR (ac.accion = 'UPDATE'
                                  AND LOWER(JSON_VALUE(ac.datos_antes, '$.estado')) IN ('0', 'false')
                                  AND LOWER(JSON_VALUE(ac.datos_despues, '$.estado')) IN ('1', 'true'))
                          )
                        ORDER BY CASE WHEN ac.accion = 'UPDATE' THEN 0 ELSE 1 END, ac.fecha_utc, ac.PK_id_evento
                    ) responsable
                    WHERE pg.FK_id_jugador = @idJugador AND pg.estado = 1 AND pg.concepto = @concepto
                      AND COALESCE(pg.fecha_vencimiento, CAST(pg.fecha_pago AS DATE)) = @clavePeriodo
                    ORDER BY pg.fecha_pago, pg.PK_id_pago";

                using SqlCommand comando = new SqlCommand(queryAbonos, conexion);
                comando.Parameters.AddWithValue("@idJugador", idJugador);
                comando.Parameters.AddWithValue("@concepto", concepto);
                comando.Parameters.AddWithValue("@clavePeriodo", clave);

                using SqlDataReader reader = comando.ExecuteReader();
                while (reader.Read())
                {
                    item.Abonos.Add(new PagoHistorialAbono
                    {
                        IdPago = Convert.ToInt32(reader["PK_id_pago"]),
                        Monto = Convert.ToDecimal(reader["monto_final"]),
                        MetodoPago = reader["metodo_pago"].ToString()?.Trim() ?? "",
                        FechaPago = Convert.ToDateTime(reader["fecha_pago"])
                        , FechaHoraRegistro = reader["fecha_hora_registro"] != DBNull.Value
                            ? Convert.ToDateTime(reader["fecha_hora_registro"])
                            : reader["auditoria_fecha_utc"] != DBNull.Value ? Convert.ToDateTime(reader["auditoria_fecha_utc"]) : null
                        , ResponsableNombre = reader["responsable_nombre"] == DBNull.Value ? null : reader["responsable_nombre"].ToString()?.Trim()
                        , ResponsableApellido = reader["responsable_apellido"] == DBNull.Value ? null : reader["responsable_apellido"].ToString()?.Trim()
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
