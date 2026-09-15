// Contract returned by GET api/players/discounts.
// The backend already resolves whether the discount is active: the front end
// only renders it.
export interface DiscountModel {
    id: number;
    playerId: number;
    playerName: string;
    category: string;
    type: string;
    percentage: number;
    startDate: string;
    endDate: string;
    isActive: boolean;
}
