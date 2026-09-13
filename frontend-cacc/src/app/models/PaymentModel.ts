// Contract returned by GET api/payments/latest and GET api/payments/pending.
// paymentDate is null while the fee has not been settled, and dueDate is null
// when no due date was recorded for it.
export interface PaymentModel {
    id: number;
    playerId: number;
    playerName: string;
    category: string;
    amount: number;
    paymentDate: string | null;
    dueDate: string | null;
    method: string;
    isPaid: boolean;
}
