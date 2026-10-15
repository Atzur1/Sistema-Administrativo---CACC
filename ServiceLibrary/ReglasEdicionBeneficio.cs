using EntityLibrary;

namespace ServiceLibrary
{
    public enum ResultadoEdicionBeneficio { Permitida, Cerrada, SoloFechaFin, FechaFinEnElPasado, InicioEnElPasado }

    // Qué se puede editar de una bonificación (beca o descuento) ya asignada. Lo que ya transcurrió es
    // historia cerrada: cambiarlo reescribiría la deuda de meses que el club ya liquidó.
    //
    // - Finalizada, cancelada o anulada: no se edita nada.
    // - En curso: solo se puede mover la fecha de fin (extenderla o acortarla, nunca antes de hoy). Para
    //   cambiar el motivo o el valor se cancela y se asigna una nueva desde hoy.
    // - Programada (todavía no empezó): se edita completa, pero no puede pasar a empezar en el pasado.
    //
    // Solo el SuperAdmin edita (lo exige el controller); el rol 2 asigna, cancela y anula.
    public static class ReglasEdicionBeneficio
    {
        public static ResultadoEdicionBeneficio Evaluar(Discount actual, Discount pedido, DateTime hoy)
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
                        || actual.StartDate.Date != pedido.StartDate.Date;
                    if (cambiaAlgoMasQueElFin) return ResultadoEdicionBeneficio.SoloFechaFin;
                    return pedido.EndDate.Date < hoy.Date
                        ? ResultadoEdicionBeneficio.FechaFinEnElPasado
                        : ResultadoEdicionBeneficio.Permitida;

                default: // Scheduled
                    return pedido.StartDate.Date < hoy.Date
                        ? ResultadoEdicionBeneficio.InicioEnElPasado
                        : ResultadoEdicionBeneficio.Permitida;
            }
        }

        public static string Mensaje(ResultadoEdicionBeneficio resultado) => resultado switch
        {
            ResultadoEdicionBeneficio.Cerrada =>
                "Esta bonificación está cerrada (finalizó, se canceló o se anuló) y no se puede editar.",
            ResultadoEdicionBeneficio.SoloFechaFin =>
                "De una bonificación en curso solo se puede cambiar la fecha de finalización. Para cambiar el motivo o el valor, cancelala y asigná una nueva desde hoy.",
            ResultadoEdicionBeneficio.FechaFinEnElPasado =>
                "La nueva fecha de finalización no puede ser anterior a hoy. Para cortarla antes de tiempo, usá Cancelar.",
            ResultadoEdicionBeneficio.InicioEnElPasado =>
                "Una bonificación programada no puede pasar a empezar en una fecha pasada.",
            _ => string.Empty
        };
    }
}
