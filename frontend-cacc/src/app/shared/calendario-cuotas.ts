// Calendario de las cuotas del club (mismo criterio que CalendarioCuotas en la API): la cuota de cada mes
// se genera el día 10 de ese mes y vence el día 10 del mes siguiente, quedando "Vencida" ese mismo día.
export const DIA_DE_CORTE = 10;

// Día en que vence la cuota del mes indicado (mesIndex 0 = enero): el 10 del mes siguiente.
export function vencimientoDelPeriodo(anio: number, mesIndex: number): Date {
  return new Date(anio, mesIndex + 1, DIA_DE_CORTE);
}

// Vencida desde el mismo día 10, inclusive.
export function estaVencida(vencimiento: Date, ahora: Date = new Date()): boolean {
  const hoy = new Date(ahora.getFullYear(), ahora.getMonth(), ahora.getDate());
  return hoy.getTime() >= vencimiento.getTime();
}
