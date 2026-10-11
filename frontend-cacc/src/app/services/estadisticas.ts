import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { GeneralSummaryModel } from '../models/GeneralSummaryModel';

@Injectable({ providedIn: 'root' })
export class EstadisticasService {
  private readonly API_URL = `${API_BASE_URL}/estadisticas`;

  constructor(private http: HttpClient) {}

  getResumenGeneral(): Observable<GeneralSummaryModel> {
    return this.http.get<GeneralSummaryModel>(`${this.API_URL}/resumen-general`);
  }
}
