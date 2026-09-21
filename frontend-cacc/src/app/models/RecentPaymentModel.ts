// One row of the "Últimos pagos" tab of the roster (HU-029).
// The screen that owns the data (Cuotas y Pagos) already formats it for display.
export interface RecentPaymentModel {
    id: number;
    idJugador: number;
    initials: string;
    name: string;
    method: string;
    amount: string;
    elapsed: string;
}
