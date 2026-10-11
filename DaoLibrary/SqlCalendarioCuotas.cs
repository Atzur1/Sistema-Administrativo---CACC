namespace DaoLibrary
{
    // Lado SQL de EntityLibrary.CalendarioCuotas: la cuota de un período (PAGOS.fecha_vencimiento = primer día
    // del mes que cubre) vence el día 10 del mes siguiente y queda vencida desde ese mismo día.
    internal static class SqlCalendarioCuotas
    {
        // Condición booleana: la cuota de la fila (alias de PAGOS) ya está vencida hoy.
        public static string EstaVencida(string aliasPagos)
            => $"DATEADD(DAY, {EntityLibrary.CalendarioCuotas.DiaDeCorte - 1}, DATEADD(MONTH, 1, {aliasPagos}.fecha_vencimiento)) <= {SqlReloj.Hoy}";
    }
}
