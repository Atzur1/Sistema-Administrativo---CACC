import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaymentModel } from '../models/PaymentModel';
import { PaymentRequestModel } from '../models/PaymentRequestModel';
import { TreasuryMetricsModel } from '../models/TreasuryMetricsModel';
import { MonthlyFeeGenerationResultModel } from '../models/MonthlyFeeGenerationResultModel';

@Injectable({
    providedIn: 'root',
})
export class PaymentService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    getLatestPayments(count: number): Observable<PaymentModel[]> {
        return this.http.get<PaymentModel[]>(`${this.API_URL}/payments/latest?count=${count}`);
    }

    getPendingFees(): Observable<PaymentModel[]> {
        return this.http.get<PaymentModel[]>(`${this.API_URL}/payments/pending`);
    }

    getPaymentsByPlayer(playerId: number): Observable<PaymentModel[]> {
        return this.http.get<PaymentModel[]>(`${this.API_URL}/payments/player/${playerId}`);
    }

    getTreasuryMetrics(): Observable<TreasuryMetricsModel> {
        return this.http.get<TreasuryMetricsModel>(`${this.API_URL}/payments/metrics`);
    }

    createPayment(request: PaymentRequestModel): Observable<PaymentModel> {
        return this.http.post<PaymentModel>(`${this.API_URL}/payments`, request);
    }

    // No body: the backend defaults to the current server month/year (HU-009)
    generateMonthlyFees(): Observable<MonthlyFeeGenerationResultModel> {
        return this.http.post<MonthlyFeeGenerationResultModel>(`${this.API_URL}/payments/generate-monthly`, {});
    }
}
