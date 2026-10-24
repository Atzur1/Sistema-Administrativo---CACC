// HU-069: how the "Programar nueva cuota mensual" form turns what the operator typed into the final amount.
// The API keeps receiving an absolute amount; the conversion happens only here.
//  - absolute:   the operator types the final amount.
//  - percentage: current tariff + that percentage, rounded to the nearest whole peso (no cents in a fee).
//  - fixed:      current tariff + that amount.
export type IncreaseMode = 'absolute' | 'percentage' | 'fixed';

// Final amount to schedule, or null when it cannot be worked out: nothing typed yet, or a relative mode
// over a destination that has no current tariff to start from.
export function calculateNewTariff(mode: IncreaseMode, value: number | null, currentTariff: number | null): number | null {
    if (value == null || !Number.isFinite(value)) {
        return null;
    }
    if (mode === 'absolute') {
        return value;
    }
    if (currentTariff == null) {
        return null;
    }
    return mode === 'percentage'
        ? Math.round(currentTariff * (1 + value / 100))
        : currentTariff + value;
}

// Only the absolute mode can be used on a destination that has never had a tariff.
export function needsCurrentTariff(mode: IncreaseMode): boolean {
    return mode !== 'absolute';
}

// What the operator types in the percentage field: digits and one decimal separator ("12,5" or "12.5"),
// at most 3 integer digits and 2 decimals. Returns what the input shows (Argentine comma) and the value
// the form keeps (dot, ready for Number()).
export function sanitizePercentage(raw: string): { display: string; value: string } {
    const cleaned = raw.replace(/[^\d.,]/g, '');
    const separatorAt = cleaned.search(/[.,]/);
    const integerPart = (separatorAt === -1 ? cleaned : cleaned.slice(0, separatorAt)).slice(0, 3);
    if (separatorAt === -1) {
        return { display: integerPart, value: integerPart };
    }
    const decimalPart = cleaned.slice(separatorAt + 1).replace(/[.,]/g, '').slice(0, 2);
    const integerOrZero = integerPart || '0';
    return { display: `${integerOrZero},${decimalPart}`, value: `${integerOrZero}.${decimalPart}` };
}
