// Contract returned by GET/POST api/players/{id}/enrollment (HU-033)
export interface EnrollmentModel {
    playerId: number;
    amount: number;
    paidAmount: number;
    pendingBalance: number;
    // yyyy-MM-dd: the date the enrollment was due (matches the player's join date)
    dueDate: string;
    payments: EnrollmentPaymentModel[];
}

export interface EnrollmentPaymentModel {
    id: number;
    amount: number;
    paymentMethod: string;
    // yyyy-MM-dd
    paymentDate: string;
}

export interface EnrollmentPaymentRequest {
    amount: number;
    paymentMethod: string;
}
