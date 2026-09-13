import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ScheduleTariffRequest, TariffModel } from '../models/TariffModel';

@Injectable({
    providedIn: 'root',
})
export class TariffService {
    readonly API_URL = 'http://localhost:5118/api';

    constructor(private http: HttpClient) {}

    getTariffHistory(): Observable<TariffModel[]> {
        return this.http.get<TariffModel[]>(`${this.API_URL}/tariffs`);
    }

    getCurrentTariffs(): Observable<TariffModel[]> {
        return this.http.get<TariffModel[]>(`${this.API_URL}/tariffs/current`);
    }

    scheduleTariff(request: ScheduleTariffRequest): Observable<TariffModel> {
        return this.http.post<TariffModel>(`${this.API_URL}/tariffs`, request);
    }
}
