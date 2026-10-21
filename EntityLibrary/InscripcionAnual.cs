namespace EntityLibrary
{
    // La inscripción es ANUAL. El año de cada jugador empieza el día en que se le asignó la inscripción (el alta) y se
    // renueva en cada aniversario, mientras esté activo. No hay una fecha única para todo el club.
    //  - Quien se da de baja sigue cubierto hasta que termina el año; no se le asigna el siguiente.
    //  - Quien vuelve dentro de ese año no la paga de nuevo; si vuelve después de que terminó, empieza un año nuevo.
    // Cada inscripción queda en PAGOS con fecha_vencimiento = el día en que empieza su año: ese día la identifica.
    public static class InscripcionAnual
    {
        // Cuántos días después del aniversario todavía se emite. Cubre una API apagada unos días sin que se pierda el año,
        // y evita cobrar un año viejo cuando un arancel de $0 pasa a tener monto.
        public const int DiasParaEmitir = 31;

        // El último aniversario (ancla + n años, con n >= 1) que ya llegó, o null si todavía no se cumplió el primer año.
        public static DateTime? UltimoAniversario(DateTime ancla, DateTime hoy)
        {
            DateTime? ultimo = null;
            for (int n = 1; ; n++)
            {
                DateTime aniversario = ancla.Date.AddYears(n);
                if (aniversario > hoy.Date) return ultimo;
                ultimo = aniversario;
            }
        }

        // Una inscripción asignada el día `emision` cubre hasta la víspera de su aniversario.
        public static bool Cubre(DateTime emision, DateTime dia) => emision.Date.AddYears(1) > dia.Date;
    }
}
