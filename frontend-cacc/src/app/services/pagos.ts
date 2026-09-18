import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface JugadorResumen {
  idJugador: number;
  nombre: string;
  apellido: string;
  dni: string;
  genero: string; // "Masculino" | "Femenino"
  categoria: string;
  nombreCompleto: string;
}

export interface PendienteJugador {
  idJugador: number;
  nombreCompleto: string;
  categoria: string;
  montoTotal: number;
  cantidadCuotas: number;
}

export interface PagoReciente {
  idPago: number;
  idJugador: number;
  nombreCompleto: string;
  metodoPago: string;
  monto: number;
  fechaPago: string;
}

export interface ResumenPagos {
  recaudadoAnioActual: number;
  pagosDelMes: number;
  cantidadPendientes: number;
}

export interface AbonoDetalle {
  monto: number;
  metodoPago: string;
  fechaPago: string;
}

export interface CuotaPendienteDetalle {
  idPago: number;
  periodo: string;
  montoOriginal: number;
  saldoPendiente: number;
  abonos: AbonoDetalle[];
}

export interface RegistrarPagoResponse {
  exito: boolean;
  idPago: number;
  idJugador: number;
  periodo: string;
  monto: number;
  metodoPago: string;
  fechaPago: string;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class PagosService {
  private apiUrl = 'http://localhost:5118/api';

  constructor(private http: HttpClient) {}

  getJugadores(): Observable<JugadorResumen[]> {
    return this.http.get<JugadorResumen[]>(`${this.apiUrl}/jugadores`);
  }

  getPendientes(): Observable<PendienteJugador[]> {
    return this.http.get<PendienteJugador[]>(`${this.apiUrl}/pagos/pendientes`);
  }

  getRecientes(top: number = 10): Observable<PagoReciente[]> {
    return this.http.get<PagoReciente[]>(`${this.apiUrl}/pagos/recientes?top=${top}`);
  }

  getResumen(): Observable<ResumenPagos> {
    return this.http.get<ResumenPagos>(`${this.apiUrl}/pagos/resumen`);
  }

  getDeuda(idJugador: number): Observable<CuotaPendienteDetalle[]> {
    return this.http.get<CuotaPendienteDetalle[]>(`${this.apiUrl}/pagos/deuda/${idJugador}`);
  }

  registrarPago(idJugador: number, periodo: string, monto: number, metodoPago: string): Observable<RegistrarPagoResponse> {
    return this.http.post<RegistrarPagoResponse>(`${this.apiUrl}/pagos/registrar`, {
      idJugador,
      periodo,
      monto,
      metodoPago,
    });
  }
}
