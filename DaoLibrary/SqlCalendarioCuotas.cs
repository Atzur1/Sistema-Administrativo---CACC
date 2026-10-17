namespace DaoLibrary
{
    // Lado SQL de EntityLibrary.CalendarioCuotas: la cuota de un período (PAGOS.fecha_vencimiento = primer día
    // del mes que cubre) vence el día 10 del mes siguiente y queda vencida desde ese mismo día.
    internal static class SqlCalendarioCuotas
    {
        // Condición booleana: la cuota de la fila (alias de PAGOS) ya está vencida hoy.
        public static string EstaVencida(string aliasPagos)
            => $"DATEADD(DAY, {EntityLibrary.CalendarioCuotas.DiaDeCorte - 1}, DATEADD(MONTH, 1, {aliasPagos}.fecha_vencimiento)) <= {SqlReloj.Hoy}";

        // Primer día del último período cuya cuota ya se emitió hoy: el mes en curso desde el 10, el anterior antes.
        // Es CalendarioCuotas.UltimoPeriodoEmitible del lado de SQL Server.
        public static string UltimoPeriodoEmitible => $@"DATEFROMPARTS(
            YEAR(CASE WHEN DAY({SqlReloj.Hoy}) >= {EntityLibrary.CalendarioCuotas.DiaDeCorte} THEN {SqlReloj.Hoy} ELSE DATEADD(MONTH, -1, {SqlReloj.Hoy}) END),
            MONTH(CASE WHEN DAY({SqlReloj.Hoy}) >= {EntityLibrary.CalendarioCuotas.DiaDeCorte} THEN {SqlReloj.Hoy} ELSE DATEADD(MONTH, -1, {SqlReloj.Hoy}) END),
            1)";
    }
}
