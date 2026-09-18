namespace DaoLibrary
{
    // Fragmentos de SQL reusados por PagosDao y EstadisticasDao para calcular cuánto le queda
    // pagar de verdad a una cuota pendiente (PAGOS), descontando el beneficio activo (si tiene)
    // de Becados y Descuentos. Centralizado acá para que ninguna de las dos clases se desalinee:
    // si un día cambia la fórmula, se cambia en un solo lugar.
    internal static class DescuentosSql
    {
        // OUTER APPLY contra "pg" (una fila de PAGOS), en un FROM PAGOS pg. Expone
        // tipo_valor/porcentaje/monto_fijo del beneficio activo cuya vigencia cubre el mes de
        // esa cuota (fecha_vencimiento), o todo NULL si no tiene ninguno.
        public const string ApplyDescuentoActivo = @"
            OUTER APPLY (
                SELECT TOP (1) jd.FK_id_descuento, jd.tipo_valor, jd.porcentaje, jd.monto_fijo
                FROM JUGADORES_DESCUENTOS jd
                WHERE jd.FK_id_jugador = pg.FK_id_jugador AND jd.estado_activo = 1
                  AND jd.fecha_inicio <= EOMONTH(pg.fecha_vencimiento) AND jd.fecha_fin >= pg.fecha_vencimiento
                ORDER BY jd.fecha_inicio DESC
            ) AS d";

        // Cuánto queda pendiente de esa cuota tras aplicar el beneficio de "d" (si no tiene
        // ninguno activo, da lo mismo que monto_final sin tocar). Puede dar negativo si el
        // beneficio es mayor a lo que faltaba — quien lo consuma tiene que clamplear a 0.
        public const string SaldoAjustadoExpr = @"
            CASE
                WHEN d.tipo_valor = '%' THEN pg.monto_final - (pg.monto_base * d.porcentaje / 100.0)
                WHEN d.tipo_valor = '$' THEN pg.monto_final - d.monto_fijo
                ELSE pg.monto_final
            END";

        // Igual que SaldoAjustadoExpr pero ya clampleado a 0 (nunca negativo).
        public const string SaldoAjustadoClampleadoExpr = $@"
            CASE WHEN ({SaldoAjustadoExpr}) < 0 THEN 0 ELSE ({SaldoAjustadoExpr}) END";
    }
}
