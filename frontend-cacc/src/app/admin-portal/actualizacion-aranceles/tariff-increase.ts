// HU-069: how the "Programar nueva cuota mensual" form reads what the operator typed.
//  - absolute:   the final amount.
//  - percentage: a percentage over the current tariff.
//  - fixed:      an amount added to the current tariff.
// HU-106: the final amount is worked out by the backend (ArancelesService.CalcularMontoNuevo), the same code that
// schedules it; the portal only sends the mode and the typed value and shows the result of the simulation.
export type IncreaseMode = 'absolute' | 'percentage' | 'fixed';

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
