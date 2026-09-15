// Contract returned by GET api/payments/metrics, for the page header counters.
export interface TreasuryMetricsModel {
    collectedThisYear: number;
    paymentsThisMonth: number;
    pendingCount: number;
}
