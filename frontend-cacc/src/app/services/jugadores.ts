import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { PlayerSummaryModel } from '../models/PlayerSummaryModel';
import { PaymentHistoryModel } from '../models/PaymentHistoryModel';

@Injectable({ providedIn: 'root' })
export class JugadoresService {
  private readonly API_URL = `${API_BASE_URL}/jugadores`;

  constructor(private http: HttpClient) {}

  getJugador(id: number): Observable<PlayerSummaryModel> {
    return this.http.get<PlayerSummaryModel>(`${this.API_URL}/${id}`);
  }

  // Baja del jugador (deja de generar cuotas desde el mes siguiente). Sin fecha, el servidor toma hoy.
  darDeBaja(id: number, fecha?: string): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${id}/baja`, fecha ? { fecha } : {});
  }

  reactivar(id: number): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${id}/reactivar`, null);
  }

  // Anula un abono del historial cargado por error (solo SuperAdmin). Su monto vuelve al saldo pendiente.
  anularPago(idPago: number, motivo: string): Observable<{ mensaje: string; saldoReabierto: number }> {
    return this.http.post<{ mensaje: string; saldoReabierto: number }>(`${API_BASE_URL}/pagos/${idPago}/anular`, { motivo });
  }

  getHistorialPagos(id: number, page: number, pageSize: number): Observable<PaymentHistoryModel> {
    return this.http.get<PaymentHistoryModel>(`${this.API_URL}/${id}/historial-pagos?page=${page}&pageSize=${pageSize}`);
  }
}
