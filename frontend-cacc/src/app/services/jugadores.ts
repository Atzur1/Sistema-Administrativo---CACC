import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { JugadorResumen } from './pagos';

export interface PagoHistorialItem {
  idPago: number;
  fechaPago: string;
  periodo: string;
  monto: number;
  metodoPago: string;
}

export interface HistorialPagosResultado {
  items: PagoHistorialItem[];
  total: number;
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class JugadoresService {
  private apiUrl = 'http://localhost:5118/api/jugadores';

  constructor(private http: HttpClient) {}

  getJugador(id: number): Observable<JugadorResumen> {
    return this.http.get<JugadorResumen>(`${this.apiUrl}/${id}`);
  }

  getHistorialPagos(id: number, page: number, pageSize: number): Observable<HistorialPagosResultado> {
    return this.http.get<HistorialPagosResultado>(`${this.apiUrl}/${id}/historial-pagos?page=${page}&pageSize=${pageSize}`);
  }
}
