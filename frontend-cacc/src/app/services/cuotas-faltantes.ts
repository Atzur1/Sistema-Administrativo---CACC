import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { MissingFeeModel, MonthWithMissingFeesModel } from '../models/MissingFeeModel';

@Injectable({ providedIn: 'root' })
export class CuotasFaltantesService {
  private readonly API_URL = `${API_BASE_URL}/cuotas-faltantes`;

  constructor(private http: HttpClient) {}

  getMeses(): Observable<MonthWithMissingFeesModel[]> {
    return this.http.get<MonthWithMissingFeesModel[]>(this.API_URL);
  }

  getDetalle(anio: number, mes: number): Observable<MissingFeeModel[]> {
    return this.http.get<MissingFeeModel[]>(`${this.API_URL}/${anio}/${mes}`);
  }

  generar(anio: number, mes: number): Observable<{ mensaje: string; creadas: number }> {
    return this.http.post<{ mensaje: string; creadas: number }>(`${this.API_URL}/${anio}/${mes}/generar`, null);
  }
}
