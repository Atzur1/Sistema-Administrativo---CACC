namespace EntityLibrary
{
    // Calendario de las cuotas mensuales del club (regla acordada con el club):
    //  - La cuota de cada mes se genera el día 10 de ese mes (DiaDeCorte).
    //  - Vence el día 10 del mes siguiente y pasa a "Vencida" ese mismo día.
    //    Ej.: la de octubre se genera el 10/10, se puede pagar hasta el 9/11 y el 10/11 queda vencida.
    //  - Se puede pagar por adelantado: un pago del 5 de octubre se imputa a octubre (el mes que el operador
    //    elige), y la cuota se emite en ese momento; el 10 el generador ya no la vuelve a crear.
    //  - El estado deportivo cuenta solo cuotas vencidas: 1 -> solo entrenamientos, 2 o más -> inhabilitado.
    //
    // Toda la API usa esta clase; el equivalente del lado de SQL Server es DaoLibrary.SqlCalendarioCuotas.
    // Los períodos se identifican por el primer día del mes que cubren (PAGOS.fecha_vencimiento).
    public static class CalendarioCuotas
    {
        public const int DiaDeCorte = 10;

        public static DateTime PrimerDiaDelMes(DateTime fecha) => new(fecha.Year, fecha.Month, 1);

        // Día en que vence la cuota del período (el 10 del mes siguiente).
        public static DateTime FechaVencimiento(DateTime periodo)
            => PrimerDiaDelMes(periodo).AddMonths(1).AddDays(DiaDeCorte - 1);

        // Vencida desde el mismo día 10 del mes siguiente, inclusive.
        public static bool EstaVencida(DateTime periodo, DateTime hoy) => hoy.Date >= FechaVencimiento(periodo);

        // ¿La cuota de este mes ya corresponde emitirse (es decir, ya llegó el 10)?
        public static bool YaSeGeneraElMesEnCurso(DateTime hoy) => hoy.Day >= DiaDeCorte;

        // Último período cuya cuota ya debería existir: el mes en curso desde el 10, el anterior antes del 10.
        public static DateTime UltimoPeriodoEmitible(DateTime hoy)
            => YaSeGeneraElMesEnCurso(hoy) ? PrimerDiaDelMes(hoy) : PrimerDiaDelMes(hoy).AddMonths(-1);

        // Primer período cuya cuota todavía no se emitió: el siguiente al último emitible.
        public static DateTime PrimerPeriodoSinEmitir(DateTime hoy)
            => UltimoPeriodoEmitible(hoy).AddMonths(1);

        // Último día del mes que cubre el período (una bonificación "hasta la cuota de X" termina ahí).
        public static DateTime FinDelPeriodo(DateTime periodo)
            => PrimerDiaDelMes(periodo).AddMonths(1).AddDays(-1);

        // Día en que se emite la cuota del período: el 10 de ese mismo mes.
        public static DateTime FechaDeEmision(DateTime periodo)
            => PrimerDiaDelMes(periodo).AddDays(DiaDeCorte - 1);

        public static readonly IReadOnlyList<string> NombresMes = new[]
        {
            "Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio",
            "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"
        };

        // "Septiembre 2026": el nombre con que se muestra un período en toda la aplicación.
        public static string NombreDelPeriodo(DateTime periodo) => $"{NombresMes[periodo.Month - 1]} {periodo.Year}";
    }
}
