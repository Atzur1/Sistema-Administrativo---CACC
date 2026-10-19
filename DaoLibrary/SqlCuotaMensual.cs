using Microsoft.Data.SqlClient;

namespace DaoLibrary
{
    // Reglas SQL de la cuota mensual, compartidas por quien la genera (PagosDao: generador mensual, aranceles,
    // cobro por adelantado) y por quien detecta los meses que quedaron sin generar (CuotasFaltantesDao). Así
    // "a quién le corresponde la cuota de un mes y por cuánto" se define una sola vez.
    internal static class SqlCuotaMensual
    {
        // Arancel del jugador `j` (con su PERSONAS `p`) para el mes que empieza en `primerDiaMes`. Un arancel
        // cargado el 5 de enero cubre igual TODO enero: por eso se compara contra el último día del mes. Primero
        // el arancel de la categoría del jugador; si no tiene, el de su género. Devuelve la columna `monto`.
        public static string ArancelDelJugador(string primerDiaMes) => $@"
            SELECT TOP (1) a.monto FROM ARANCELES a
            WHERE a.vigente_desde <= EOMONTH({primerDiaMes})
              AND (a.FK_id_categoria = j.FK_id_categoria
                   OR (a.FK_id_categoria IS NULL AND a.genero = LTRIM(RTRIM(p.genero))))
            ORDER BY CASE WHEN a.FK_id_categoria IS NULL THEN 1 ELSE 0 END, a.vigente_desde DESC";

        // El jugador estuvo en el club algún día del mes: dado de alta antes de que termine y sin baja anterior
        // a que empiece. Sin esto, un arancel de un mes pasado le generaba deuda a quien todavía no había entrado
        // al club, y quien se fue seguía acumulando cuotas para siempre.
        public static string JugadorEnElClub(string primerDiaMes) => $@"
            (j.fecha_alta IS NULL OR j.fecha_alta <= EOMONTH({primerDiaMes}))
            AND (j.fecha_baja IS NULL OR j.fecha_baja >= {primerDiaMes})";

        // Todavía no tiene ninguna fila de cuota de ese mes (pendiente, pagada o pagada por adelantado). Solo
        // cuotas (HU-033): una inscripción en ese mes no reemplaza la cuota.
        public static string SinCuotaEnElMes(string primerDiaMes, string pista = "") => $@"
            NOT EXISTS (
                SELECT 1 FROM PAGOS p2 {pista}
                WHERE p2.FK_id_jugador = j.PK_id_jugador AND p2.concepto = 'Cuota'
                  AND p2.fecha_vencimiento >= {primerDiaMes}
                  AND p2.fecha_vencimiento < DATEADD(MONTH, 1, {primerDiaMes}))";

        // Emite la cuota del mes a quien le corresponde y todavía no la tiene. Los filtros acotan el alcance
        // (un arancel de género, uno de categoría, un solo jugador); sin filtros alcanza a todos. Se puede
        // repetir sin duplicar nada. Devuelve cuántas cuotas creó.
        //
        // A propósito NO se aplica ningún beneficio de Becados y Descuentos: monto_base y monto_final quedan con
        // el arancel CRUDO. El beneficio se resuelve cada vez que se lee o cobra la cuota (DescuentosSql);
        // aplicarlo acá también lo duplicaba.
        public static int Generar(SqlConnection conexion, SqlTransaction transaccion, string? genero, int? idCategoria, int? idJugador, int mes, int anio)
        {
            string query = $@"
                -- PK_id_pago sale de la secuencia (default de la columna), uno por fila.
                INSERT INTO PAGOS (FK_id_jugador, monto_base, FK_id_jugador_descuento, monto_final, fecha_pago, metodo_pago, fecha_vencimiento, estado)
                SELECT j.PK_id_jugador, arancel.monto, NULL, arancel.monto, NULL, NULL, @primerDiaMes, 0
                FROM JUGADORES j
                JOIN PERSONAS p ON j.FK_id_persona = p.PK_id_persona
                CROSS APPLY ({ArancelDelJugador("@primerDiaMes")}) AS arancel
                WHERE (@genero IS NULL OR LTRIM(RTRIM(p.genero)) = @genero)
                  AND (@idCategoria IS NULL OR j.FK_id_categoria = @idCategoria)
                  AND (@idJugador IS NULL OR j.PK_id_jugador = @idJugador)
                  AND {JugadorEnElClub("@primerDiaMes")}
                  AND {SinCuotaEnElMes("@primerDiaMes", "WITH (UPDLOCK, HOLDLOCK)")};";
            using SqlCommand comando = new SqlCommand(query, conexion, transaccion);
            comando.Parameters.Add("@genero", System.Data.SqlDbType.VarChar, 20).Value = (object?)genero ?? DBNull.Value;
            comando.Parameters.Add("@idCategoria", System.Data.SqlDbType.Int).Value = (object?)idCategoria ?? DBNull.Value;
            comando.Parameters.Add("@idJugador", System.Data.SqlDbType.Int).Value = (object?)idJugador ?? DBNull.Value;
            comando.Parameters.Add("@primerDiaMes", System.Data.SqlDbType.Date).Value = new DateTime(anio, mes, 1);
            return comando.ExecuteNonQuery();
        }
    }
}
