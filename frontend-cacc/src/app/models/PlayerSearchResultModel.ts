// Contract returned by GET api/players/search?term= (HU-023).
export interface PlayerSearchResultModel {
    id: number;
    firstName: string;
    lastName: string;
    dni: string;
    categoryName: string;
}
