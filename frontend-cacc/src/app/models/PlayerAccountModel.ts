import { EstadoDeportivo } from './PaymentModel';

// Contract returned by GET api/pagos/player-accounts (HU-029).
// The backend already decides who owes: the front end only renders it.
export interface PlayerAccountModel {
    playerId: number;
    firstName: string;
    lastName: string;
    dni: string;
    category: string;

    // 0 when the player is up to date
    amountOwed: number;

    // Installments that still have a real balance
    pendingInstallments: number;

    // Of those, the ones already overdue (from the 10th of the following month)
    overdueInstallments: number;

    // "yyyy-MM-dd" when the player left the club (they can still owe), or null while active
    leaveDate?: string | null;

    // Sport status computed by the backend (same rule as the integration API): the screen shows it, it does not recompute it
    sportStatus: EstadoDeportivo;
}
