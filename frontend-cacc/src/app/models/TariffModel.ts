// Contract returned by GET api/tariffs and GET api/tariffs/current, and by
// the object returned from POST api/tariffs.
// branch is 'M' (Masculino) or 'F' (Femenino): each branch has its own fully
// independent validity timeline. validTo is null while the tariff is open
// ended (isActive true); once a later tariff is scheduled for the same
// branch, validTo is fixed to the day before it starts and never changes
// again, so fees already issued under it keep their persisted amount.
export interface TariffModel {
    id: number;
    branch: 'M' | 'F';
    amount: number;
    validFrom: string;
    validTo: string | null;
    isActive: boolean;
}

// Body for POST api/tariffs: schedules a new tariff for one branch without
// referencing any existing fee or payment.
export interface ScheduleTariffRequest {
    branch: 'M' | 'F';
    amount: number;
    validFrom: string;
}
