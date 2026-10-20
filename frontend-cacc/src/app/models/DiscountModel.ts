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

    // Both mandatory since HU-012, in yyyy-MM-dd. A benefit covers whole fees: startDate is the first day
    // of the month of the first fee it applies to and endDate the last day of the month of the last one
    // (the ones loaded before that rule can start or end mid-month; they are read by month all the same).
    startDate: string;
    endDate: string;

    // The day it was loaded (yyyy-MM-dd), set by the system. Not the same as startDate: a benefit loaded
    // today can start with next month's fee. Null for the ones loaded before it was recorded.
    assignmentDate?: string | null;

    // Where the benefit stands today, resolved by the server against its own
    // clock. This is the only field to read to know the situation: the browser
    // never decides it, so two administrators in different time zones see the
    // same thing.
    status: DiscountStatus;

    // Derived from status by the API. Kept for what HU-014 left behind; new code
    // should read status.
    isActive: boolean;
    responsableNombre?: string | null;
    responsableApellido?: string | null;
}

export type BenefitValueType = '%' | '$';

export type DiscountStatus = 'Scheduled' | 'Active' | 'Expired' | 'Cancelled' | 'Voided';

// The state as the club reads it. Code travels in English, the screen speaks
// Spanish.
const STATUS_LABELS: Record<DiscountStatus, string> = {
    Scheduled: 'Programada',
    Active: 'Activa',
    Expired: 'Expirada',
    Cancelled: 'Cancelada',
    // Asignado por error: a diferencia de Cancelada, no cuenta ni para los meses que ya
    // pasaron — el backend lo excluye por completo del cálculo de deuda.
    Voided: 'Anulada',
};

export function statusLabel(status: DiscountStatus): string {
    return STATUS_LABELS[status] ?? status;
}

// Tone of the shared status tag (.etiqueta-estado in styles.css, HU-093). Keeps the colours this
// screen already used on purpose: cancelled (taken down by an admin) in red, voided ("should not
// have happened", already resolved) and expired in neutral grey, scheduled as a future fact.
const STATUS_TONES: Record<DiscountStatus, string> = {
    Active: 'tono-exito',
    Scheduled: 'tono-info',
    Expired: 'tono-neutro',
    Cancelled: 'tono-peligro',
    Voided: 'tono-neutro',
};

export function statusToneClass(status: DiscountStatus): string {
    return STATUS_TONES[status] ?? 'tono-neutro';
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

const MONTH_NAMES = [
    'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
    'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

// The fee a date belongs to, the way the club names it: "2026-10-01" -> "Octubre 2026". Same split-by-hand
// reason as formatIsoDate.
export function periodLabel(isoDate: string): string {
    if (!isoDate) {
        return '';
    }
    const [year, month] = isoDate.split('-');
    return `${MONTH_NAMES[Number(month) - 1] ?? ''} ${year}`.trim();
}

// The validity the way the financial card shows it, by fee: "Octubre 2026 a Diciembre 2026"
export function formatValidity(discount: DiscountModel): string {
    return `${periodLabel(discount.startDate)} a ${periodLabel(discount.endDate)}`;
}

// From which fee a benefit can start for a player today (GET players/{id}/discount/first-period).
// alreadyIssued: that fee already exists (the current month's, with nothing paid yet) instead of being issued
// later, on issueDate.
export interface FirstPeriodModel {
    period: string;
    issueDate: string;
    alreadyIssued: boolean;
}

export interface PeriodOption {
    value: string;
    label: string;
}

// The last day of the month of an ISO date: "2026-12-01" -> "2026-12-31".
export function lastDayOfMonth(isoDate: string): string {
    const [year, month] = isoDate.split('-').map(Number);
    const day = new Date(year, month, 0).getDate();
    return `${year}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

// The first day of the month that is `offset` months from the one of an ISO date.
export function addMonthsToPeriod(isoDate: string, offset: number): string {
    const [year, month] = isoDate.split('-').map(Number);
    const moved = new Date(year, month - 1 + offset, 1);
    return `${moved.getFullYear()}-${String(moved.getMonth() + 1).padStart(2, '0')}-01`;
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
