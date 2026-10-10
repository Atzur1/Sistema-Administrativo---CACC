import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { JugadorResumen } from './pagos';
import { API_BASE_URL } from './api-url';

export interface PagoHistorialAbono {
  idPago: number; // fila de PAGOS del abono: la que se anula con anularPago()
  monto: number;
  metodoPago: string;
  fechaPago: string;
  fechaHoraRegistro?: string | null;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
}

export interface PagoHistorialItem {
  periodo: string;
  concepto: 'Cuota' | 'Inscripcion';
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
  private apiUrl = `${API_BASE_URL}/jugadores`;

  constructor(private http: HttpClient) {}

  getJugador(id: number): Observable<JugadorResumen> {
    return this.http.get<JugadorResumen>(`${this.apiUrl}/${id}`);
  }

  // Baja del jugador (deja de generar cuotas desde el mes siguiente). Sin fecha, el servidor toma hoy.
  darDeBaja(id: number, fecha?: string): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.apiUrl}/${id}/baja`, fecha ? { fecha } : {});
  }

  reactivar(id: number): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.apiUrl}/${id}/reactivar`, null);
  }

  // Anula un abono del historial cargado por error (solo SuperAdmin). Su monto vuelve al saldo pendiente.
  anularPago(idPago: number, motivo: string): Observable<{ mensaje: string; saldoReabierto: number }> {
    return this.http.post<{ mensaje: string; saldoReabierto: number }>(`${API_BASE_URL}/pagos/${idPago}/anular`, { motivo });
  }

  getHistorialPagos(id: number, page: number, pageSize: number): Observable<HistorialPagosResultado> {
    return this.http.get<HistorialPagosResultado>(`${this.apiUrl}/${id}/historial-pagos?page=${page}&pageSize=${pageSize}`);
  }
}
