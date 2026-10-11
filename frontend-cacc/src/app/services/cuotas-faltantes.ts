import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';

// Un mes que quedó con cuotas sin generar (por ejemplo, porque el sistema estuvo apagado el día 10).
export interface MesConCuotasFaltantes {
  periodo: string; // "2026-09-01": primer día del mes
  cantidad: number;
  montoTotal: number;
}

// Una cuota que se generaría: a quién y por cuánto (su arancel de ese mes).
export interface CuotaFaltante {
  idJugador: number;
  nombreCompleto: string;
  dni: string;
  categoria: string;
  monto: number;
}

// Aviso de cuotas faltantes del Resumen General (solo SuperAdmin). Nada se genera solo.
@Injectable({ providedIn: 'root' })
export class CuotasFaltantesService {
  private apiUrl = `${API_BASE_URL}/cuotas-faltantes`;

  constructor(private http: HttpClient) {}

  getMeses(): Observable<MesConCuotasFaltantes[]> {
    return this.http.get<MesConCuotasFaltantes[]>(this.apiUrl);
  }

  getDetalle(anio: number, mes: number): Observable<CuotaFaltante[]> {
    return this.http.get<CuotaFaltante[]>(`${this.apiUrl}/${anio}/${mes}`);
  }

  generar(anio: number, mes: number): Observable<{ mensaje: string; creadas: number }> {
    return this.http.post<{ mensaje: string; creadas: number }>(`${this.apiUrl}/${anio}/${mes}/generar`, null);
  }
}
