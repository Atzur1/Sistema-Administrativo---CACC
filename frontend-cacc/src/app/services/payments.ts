import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PaymentModel } from '../models/PaymentModel';
import { TreasuryMetricsModel } from '../models/TreasuryMetricsModel';

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

    getTreasuryMetrics(): Observable<TreasuryMetricsModel> {
        return this.http.get<TreasuryMetricsModel>(`${this.API_URL}/payments/metrics`);
    }
}
