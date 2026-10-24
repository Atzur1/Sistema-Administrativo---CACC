import { calculateNewTariff, needsCurrentTariff, sanitizePercentage } from './tariff-increase';

describe('calculateNewTariff', () => {
  describe('absolute mode', () => {
    it('returns the typed amount as is', () => {
      expect(calculateNewTariff('absolute', 92000, 85000)).toBe(92000);
    });

    it('does not need a current tariff', () => {
      expect(calculateNewTariff('absolute', 92000, null)).toBe(92000);
      expect(needsCurrentTariff('absolute')).toBe(false);
    });
  });

  describe('percentage mode', () => {
    it('adds the percentage to the current tariff', () => {
      expect(calculateNewTariff('percentage', 10, 85000)).toBe(93500);
    });

    it('$85.000 + 12,5% = $95.625', () => {
      expect(calculateNewTariff('percentage', 12.5, 85000)).toBe(95625);
    });

    it('rounds to the nearest whole peso', () => {
      // 85.555 * 1,10 = 94.110,5 -> 94.111 ; 85.333 * 1,10 = 93.866,3 -> 93.866
      expect(calculateNewTariff('percentage', 10, 85555)).toBe(94111);
      expect(calculateNewTariff('percentage', 10, 85333)).toBe(93866);
      expect(Number.isInteger(calculateNewTariff('percentage', 7.25, 61999))).toBe(true);
    });

    it('cannot be calculated without a current tariff', () => {
      expect(calculateNewTariff('percentage', 10, null)).toBeNull();
      expect(needsCurrentTariff('percentage')).toBe(true);
    });
  });

  describe('fixed mode', () => {
    it('adds the amount to the current tariff', () => {
      expect(calculateNewTariff('fixed', 10000, 85000)).toBe(95000);
    });

    it('cannot be calculated without a current tariff', () => {
      expect(calculateNewTariff('fixed', 10000, null)).toBeNull();
      expect(needsCurrentTariff('fixed')).toBe(true);
    });
  });

  it('returns null while nothing has been typed', () => {
    expect(calculateNewTariff('absolute', null, 85000)).toBeNull();
    expect(calculateNewTariff('percentage', Number.NaN, 85000)).toBeNull();
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
