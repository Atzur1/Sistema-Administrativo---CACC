// Contract sent to POST api/payments. The payment date and the user who
// registers it are set by the server from its clock and the session token.
export interface PaymentRequestModel {
    playerId: number;
    periodYear: number;
    periodMonth: number;
    amount: number;
    method: string;
    reference: string | null;
}
