import { needsCurrentTariff, sanitizePercentage } from './tariff-increase';

// HU-106: el monto final lo calcula el backend (ArancelesService.CalcularMontoNuevo, con sus pruebas, incluidos los
// casos de redondeo que antes se probaban acá). Queda lo que el formulario necesita antes de enviar.
describe('needsCurrentTariff', () => {
  it('only the relative modes need a current tariff', () => {
    expect(needsCurrentTariff('absolute')).toBe(false);
    expect(needsCurrentTariff('percentage')).toBe(true);
    expect(needsCurrentTariff('fixed')).toBe(true);
  });
});

describe('sanitizePercentage', () => {
  it('accepts a comma or a dot as decimal separator and shows a comma', () => {
    expect(sanitizePercentage('12,5')).toEqual({ display: '12,5', value: '12.5' });
    expect(sanitizePercentage('12.5')).toEqual({ display: '12,5', value: '12.5' });
  });

  it('keeps one separator, two decimals and three integer digits', () => {
    expect(sanitizePercentage('12,5,7')).toEqual({ display: '12,57', value: '12.57' });
    expect(sanitizePercentage('1,2345')).toEqual({ display: '1,23', value: '1.23' });
    expect(sanitizePercentage('12345')).toEqual({ display: '123', value: '123' });
  });

  it('drops letters and signs', () => {
    expect(sanitizePercentage('-1a0%')).toEqual({ display: '10', value: '10' });
  });

  it('a leading separator becomes "0,"', () => {
    expect(sanitizePercentage(',5')).toEqual({ display: '0,5', value: '0.5' });
  });
});
