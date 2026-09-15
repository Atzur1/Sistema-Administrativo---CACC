// Contract returned by the discount endpoints of api/players.
// The backend already resolves whether the discount is in force: the front end
// only renders it.
export interface DiscountModel {
    id: number;
    playerId: number;
    playerName: string;
    category: string;

    // The reason, as it reads in the TIPO_DESCUENTO catalogue
    type: string;

    // '%' or '$'. Says which of the two values below carries the benefit.
    valueType: BenefitValueType;

    // Only one of these arrives with a value; the other comes as null, because a
    // benefit is either percentage based or a fixed amount, never both.
    percentage: number | null;
    fixedAmount: number | null;

    startDate: string;

    // Empty when the benefit has no expiry date
    endDate: string;

    isActive: boolean;
}

export type BenefitValueType = '%' | '$';

// Body sent to assign or edit a benefit. The player travels in the URL, so it is
// not part of the payload.
export interface DiscountRequest {
    reason: string;
    valueType: BenefitValueType;
    percentage: number | null;
    fixedAmount: number | null;
    startDate: string | null;
    endDate: string | null;
}

// Reads the benefit the way the club talks about it: "50 %" or "$15.000"
export function formatBenefitValue(discount: DiscountModel): string {
    if (discount.valueType === '$') {
        return discount.fixedAmount === null
            ? '—'
            : `$${discount.fixedAmount.toLocaleString('es-AR', { maximumFractionDigits: 2 })}`;
    }

    if (discount.percentage === null) {
        return '—';
    }

    // 50.00 reads as "50 %" and 33.33 keeps its decimals
    const percentage = Number(discount.percentage);
    const rendered = Number.isInteger(percentage) ? percentage.toString() : percentage.toFixed(2);

    return `${rendered} %`;
}
