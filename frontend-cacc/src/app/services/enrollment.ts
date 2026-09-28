import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { EnrollmentModel, EnrollmentPaymentModel, EnrollmentPaymentRequest } from '../models/EnrollmentModel';
import { API_BASE_URL } from './api-url';

@Injectable({
    providedIn: 'root',
})
export class EnrollmentService {
    readonly API_URL = API_BASE_URL;

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
