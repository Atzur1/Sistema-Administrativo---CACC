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
}
