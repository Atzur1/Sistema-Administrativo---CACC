using EntityLibrary;

namespace ServiceLibrary
{
    public enum ResultadoEdicionBeneficio { Permitida, Cerrada, SoloFechaFin, FechaFinEnElPasado, InicioEnElPasado }

    // Qué se puede editar de una bonificación (beca o descuento) ya asignada. Lo que ya transcurrió es
    // historia cerrada: cambiarlo reescribiría la deuda de meses que el club ya liquidó.
    //
    // Una bonificación cubre cuotas enteras, así que "lo que ya transcurrió" son las cuotas ya emitidas.
    //
    // - Finalizada, cancelada o anulada: no se edita nada.
    // - En curso: solo se puede mover la fecha de fin (extenderla o acortarla), pero no hasta dejar sin
    //   beneficio una cuota que ya se emitió. Para cambiar el motivo o el valor se cancela y se asigna una nueva.
    // - Programada (todavía no empezó): se edita completa, pero no puede pasar a empezar antes de la primera
    //   cuota que se puede becar (primerPeriodoPermitido).
    //
    // Solo el SuperAdmin edita (lo exige el controller); el rol 2 asigna, cancela y anula.
    public static class ReglasEdicionBeneficio
    {
        // Las fechas de pedido ya vienen llevadas a mes completo (ver BeneficiosService). La de inicio de una
        // bonificación vieja puede ser a mitad de mes: se compara por mes.
        public static ResultadoEdicionBeneficio Evaluar(Discount actual, Discount pedido, DateTime hoy, DateTime primerPeriodoPermitido)
        {
            switch (actual.Status)
            {
                case DiscountStatus.Expired:
                case DiscountStatus.Cancelled:
                case DiscountStatus.Voided:
                    return ResultadoEdicionBeneficio.Cerrada;

                case DiscountStatus.Active:
                    bool cambiaAlgoMasQueElFin =
                        !string.Equals(actual.Type, pedido.Type, StringComparison.OrdinalIgnoreCase)
                        || actual.ValueType != pedido.ValueType
                        || actual.Percentage != pedido.Percentage
                        || actual.FixedAmount != pedido.FixedAmount
                        || CalendarioCuotas.PrimerDiaDelMes(actual.StartDate) != CalendarioCuotas.PrimerDiaDelMes(pedido.StartDate);
                    if (cambiaAlgoMasQueElFin) return ResultadoEdicionBeneficio.SoloFechaFin;
                    return pedido.EndDate.Date < CalendarioCuotas.FinDelPeriodo(CalendarioCuotas.UltimoPeriodoEmitible(hoy))
                        ? ResultadoEdicionBeneficio.FechaFinEnElPasado
                        : ResultadoEdicionBeneficio.Permitida;

                default: // Scheduled
                    return pedido.StartDate.Date < primerPeriodoPermitido.Date
                        ? ResultadoEdicionBeneficio.InicioEnElPasado
                        : ResultadoEdicionBeneficio.Permitida;
            }
        }

        public static string Mensaje(ResultadoEdicionBeneficio resultado) => resultado switch
        {
            ResultadoEdicionBeneficio.Cerrada =>
                "Esta bonificación está cerrada (finalizó, se canceló o se anuló) y no se puede editar.",
            ResultadoEdicionBeneficio.SoloFechaFin =>
                "De una bonificación en curso solo se puede cambiar hasta qué cuota rige. Para cambiar el motivo o el valor, cancelala y asigná una nueva.",
            ResultadoEdicionBeneficio.FechaFinEnElPasado =>
                "La bonificación no puede terminar antes de la última cuota ya emitida. Para cortarla antes de tiempo, usá Cancelar.",
            ResultadoEdicionBeneficio.InicioEnElPasado =>
                "Una bonificación programada no puede pasar a empezar con una cuota que ya no se puede becar.",
            _ => string.Empty
        };
    }
}
