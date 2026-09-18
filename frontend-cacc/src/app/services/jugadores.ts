import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { JugadorResumen } from './pagos';

export interface PagoHistorialAbono {
  monto: number;
  metodoPago: string;
  fechaPago: string;
}

export interface PagoHistorialItem {
  periodo: string;
  montoTotal: number; // Lo efectivamente cobrado
  montoOriginal: number; // El valor completo de la cuota, antes del beneficio (si tuvo uno)
  tieneBeneficio: boolean;
  motivoBeneficio: string | null;
  tipoValorBeneficio: string | null; // "%" o "$"
  porcentajeBeneficio: number | null;
  montoFijoBeneficio: number | null;
  abonos: PagoHistorialAbono[];
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
