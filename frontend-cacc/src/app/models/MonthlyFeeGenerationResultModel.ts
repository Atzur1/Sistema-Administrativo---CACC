// Contract returned by POST api/payments/generate-monthly.
export interface MonthlyFeeGenerationResultModel {
    totalGenerated: number;
    totalSkipped: number;
    periodName: string;
}
