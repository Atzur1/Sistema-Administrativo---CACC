// Contract returned by GET api/players/search?term= (HU-023).
export interface PlayerSearchResultModel {
    id: number;
    firstName: string;
    lastName: string;
    dni: string;
    categoryName: string;
    // "yyyy-MM-dd" when the player left the club, or null while active
    leaveDate?: string | null;
}
