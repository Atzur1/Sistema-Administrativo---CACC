// Contract returned by the payments endpoints (latest, pending, by player and
// the one that registers a payment). paymentDate is null while the fee has not
// been settled and dueDate is null when no due date was recorded for it.
// period ("yyyy-MM"), reference and registeredAt are null for historical
// payments, recorded before that data existed.
export interface PaymentModel {
    id: number;
    playerId: number;
    playerName: string;
    category: string;
    amount: number;
    paymentDate: string | null;
    dueDate: string | null;
    period: string | null;
    method: string;
    isPaid: boolean;
    reference: string | null;
    registeredAt: string | null;
}
