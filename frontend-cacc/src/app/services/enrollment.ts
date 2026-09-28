import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { EnrollmentModel, EnrollmentPaymentModel, EnrollmentPaymentRequest } from '../models/EnrollmentModel';

@Injectable({
    providedIn: 'root',
})
export class EnrollmentService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    getEnrollmentByPlayer(playerId: number): Observable<EnrollmentModel> {
        return this.http.get<EnrollmentModel>(`${this.API_URL}/players/${playerId}/enrollment`);
    }

    registerEnrollmentPayment(playerId: number, request: EnrollmentPaymentRequest): Observable<EnrollmentPaymentModel> {
        return this.http.post<EnrollmentPaymentModel>(
            `${this.API_URL}/players/${playerId}/enrollment/payments`,
            request
        );
    }
}
