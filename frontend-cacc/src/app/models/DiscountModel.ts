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

    // Both mandatory since HU-012, in yyyy-MM-dd
    startDate: string;
    endDate: string;

    // Where the benefit stands today, resolved by the server against its own
    // clock. This is the only field to read to know the situation: the browser
    // never decides it, so two administrators in different time zones see the
    // same thing.
    status: DiscountStatus;

    // Derived from status by the API. Kept for what HU-014 left behind; new code
    // should read status.
    isActive: boolean;
}

export type BenefitValueType = '%' | '$';

export type DiscountStatus = 'Scheduled' | 'Active' | 'Expired' | 'Cancelled';

// The state as the club reads it. Code travels in English, the screen speaks
// Spanish.
const STATUS_LABELS: Record<DiscountStatus, string> = {
    Scheduled: 'Programada',
    Active: 'Activa',
    Expired: 'Expirada',
    Cancelled: 'Cancelada',
};

export function statusLabel(status: DiscountStatus): string {
    return STATUS_LABELS[status] ?? status;
}

// Modifier for the pill that shows the state
export function statusToneClass(status: DiscountStatus): string {
    return `status-${status.toLowerCase()}`;
}

// Body sent to assign or edit a benefit. The player travels in the URL, so it is
// not part of the payload.
export interface DiscountRequest {
    reason: string;
    valueType: BenefitValueType;
    percentage: number | null;
    fixedAmount: number | null;

    // Mandatory since HU-012: every benefit runs inside an authorised period
    startDate: string;
    endDate: string;
}

// An ISO date (yyyy-MM-dd) as it is read locally. Split by hand instead of
// through Date: parsing "2026-10-01" as a Date lands on UTC midnight and, west
// of Greenwich, prints the day before.
export function formatIsoDate(isoDate: string): string {
    if (!isoDate) {
        return '';
    }
    const [year, month, day] = isoDate.split('-');
    return `${day}/${month}/${year}`;
}

// The validity the way the financial card shows it: "01/10/2026 - 31/12/2026"
export function formatValidity(discount: DiscountModel): string {
    return `${formatIsoDate(discount.startDate)} - ${formatIsoDate(discount.endDate)}`;
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
