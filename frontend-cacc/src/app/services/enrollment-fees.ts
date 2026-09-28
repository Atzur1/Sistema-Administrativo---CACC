import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { EnrollmentFeeModel, EnrollmentFeeRequest } from '../models/EnrollmentFeeModel';

@Injectable({
    providedIn: 'root',
})
export class EnrollmentFeeService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    // Past, current and scheduled fees, newest first
    getAllEnrollmentFees(): Observable<EnrollmentFeeModel[]> {
        return this.http.get<EnrollmentFeeModel[]>(`${this.API_URL}/enrollmentfees`);
    }

    createEnrollmentFee(request: EnrollmentFeeRequest): Observable<EnrollmentFeeModel> {
        return this.http.post<EnrollmentFeeModel>(`${this.API_URL}/enrollmentfees`, request);
    }
}
