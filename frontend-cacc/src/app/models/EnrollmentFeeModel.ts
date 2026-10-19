// Contract returned by api/enrollmentfees (HU-033): the one-time enrollment fee, one history
// per gender. A gender with no fee in force, or with one of 0, is not charged (today: women).
// The backend resolves end date and status against its own clock; the front end only renders them.
export type EnrollmentFeeGender = 'Masculino' | 'Femenino';

export interface EnrollmentFeeModel {
    id: number;
    gender: EnrollmentFeeGender;
    amount: number;

    // yyyy-MM-dd
    startDate: string;

    // null while no later fee has been scheduled
    endDate: string | null;

    status: EnrollmentFeeStatus;
    responsibleName?: string | null;
    responsibleSurname?: string | null;
    registeredAtUtc?: string | null;
}

export type EnrollmentFeeStatus = 'Scheduled' | 'Current' | 'Previous';

// Body sent to schedule a new enrollment fee (amount 0 = that gender is not charged from that date)
export interface EnrollmentFeeRequest {
    gender: EnrollmentFeeGender;
    amount: number;
    startDate: string;
}

// Code travels in English, the screen speaks Spanish. Same words the monthly fee
// history of the screen already uses.
const STATUS_LABELS: Record<EnrollmentFeeStatus, string> = {
    Scheduled: 'Programado',
    Current: 'Vigente',
    Previous: 'Anterior',
};

// Pill modifiers the Actualización de aranceles screen already defines
const STATUS_CLASSES: Record<EnrollmentFeeStatus, string> = {
    Scheduled: 'status-scheduled',
    Current: 'status-current',
    Previous: 'status-previous',
};

export function enrollmentFeeStatusLabel(status: EnrollmentFeeStatus): string {
    return STATUS_LABELS[status] ?? status;
}

export function enrollmentFeeStatusClass(status: EnrollmentFeeStatus): string {
    return STATUS_CLASSES[status] ?? 'status-previous';
}

export function enrollmentFeeLabel(gender: EnrollmentFeeGender): string {
    return gender === 'Femenino' ? 'Inscripción femenina' : 'Inscripción masculina';
}

export function findCurrentEnrollmentFee(fees: EnrollmentFeeModel[], gender: EnrollmentFeeGender = 'Masculino'): EnrollmentFeeModel | null {
    return fees.find((f) => f.status === 'Current' && f.gender === gender) ?? null;
}

// The closest fee still waiting to start
export function findNextEnrollmentFee(fees: EnrollmentFeeModel[], gender: EnrollmentFeeGender = 'Masculino'): EnrollmentFeeModel | null {
    const scheduled = fees
        .filter((f) => f.status === 'Scheduled' && f.gender === gender)
        .sort((a, b) => a.startDate.localeCompare(b.startDate));
    return scheduled[0] ?? null;
}

// Only one fee per gender can start on a given day: the API refuses the second one
export function isEnrollmentFeeDateTaken(fees: EnrollmentFeeModel[], startDate: string, gender: EnrollmentFeeGender = 'Masculino'): boolean {
    return fees.some((f) => f.startDate === startDate && f.gender === gender);
}
