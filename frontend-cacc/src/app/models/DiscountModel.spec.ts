import {
  DiscountModel,
  addMonthsToPeriod,
  formatValidity,
  lastDayOfMonth,
  periodLabel,
} from './DiscountModel';

// Una bonificación cubre cuotas enteras: las fechas se leen por mes ("Octubre 2026"), no por día.
describe('DiscountModel - períodos (cuotas)', () => {
  it('nombra la cuota que contiene una fecha, sin correrla por la zona horaria', () => {
    expect(periodLabel('2026-10-01')).toBe('Octubre 2026');
    expect(periodLabel('2026-12-31')).toBe('Diciembre 2026');
    expect(periodLabel('2027-01-01')).toBe('Enero 2027');
    expect(periodLabel('')).toBe('');
  });

  it('lleva una fecha al último día de su mes, años bisiestos incluidos', () => {
    expect(lastDayOfMonth('2026-10-01')).toBe('2026-10-31');
    expect(lastDayOfMonth('2026-11-17')).toBe('2026-11-30');
    expect(lastDayOfMonth('2028-02-01')).toBe('2028-02-29');
    expect(lastDayOfMonth('2027-02-10')).toBe('2027-02-28');
  });

  it('suma meses al período y siempre devuelve el primer día', () => {
    expect(addMonthsToPeriod('2026-10-01', 0)).toBe('2026-10-01');
    expect(addMonthsToPeriod('2026-10-27', 0)).toBe('2026-10-01');
    expect(addMonthsToPeriod('2026-10-01', 3)).toBe('2027-01-01');
    expect(addMonthsToPeriod('2026-01-01', -1)).toBe('2025-12-01');
  });

  it('muestra la vigencia por cuota', () => {
    const discount = { startDate: '2026-10-01', endDate: '2026-12-31' } as DiscountModel;

    expect(formatValidity(discount)).toBe('Octubre 2026 a Diciembre 2026');
  });

  // Las cargadas antes de la regla de cuotas enteras pueden empezar o terminar a mitad de mes: rigen por mes igual.
  it('lee por mes una bonificación vieja que empezaba a mitad de mes', () => {
    const vieja = { startDate: '2026-09-27', endDate: '2026-10-02' } as DiscountModel;

    expect(formatValidity(vieja)).toBe('Septiembre 2026 a Octubre 2026');
  });
});
