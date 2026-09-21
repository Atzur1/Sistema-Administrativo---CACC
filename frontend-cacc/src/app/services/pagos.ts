import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PlayerAccountModel } from '../models/PlayerAccountModel';

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
  dni: string;
  idCategoria: number;
  categoria: string;
  montoTotal: number;
  cantidadCuotas: number;
}

export interface CategoriaDeuda {
  idCategoria: number;
  categoria: string;
  montoTotal: number;
  cantidadJugadores: number;
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
  // HU-019: indicador destacado "Deuda Global Total" — suma de cada cuota pendiente por su
  // monto ya congelado al mes de emisión (no el arancel vigente hoy), y cantidad de jugadores
  // únicos con al menos una cuota con saldo real > 0.
  deudaGlobalTotal: number;
  jugadoresMorosos: number;
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
  saldoPendiente: number; // Ya con el beneficio de Becados y Descuentos aplicado, si tiene uno
  tieneBeneficio: boolean;
  motivoBeneficio: string | null;
  tipoValorBeneficio: string | null; // "%" o "$"
  porcentajeBeneficio: number | null;
  montoFijoBeneficio: number | null;
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

  // idCategoria: HU-020, acota el padrón a esa categoría/división del lado del servidor.
  getPendientes(idCategoria?: number | null): Observable<PendienteJugador[]> {
    const url = idCategoria != null
      ? `${this.apiUrl}/pagos/pendientes?idCategoria=${idCategoria}`
      : `${this.apiUrl}/pagos/pendientes`;
    return this.http.get<PendienteJugador[]>(url);
  }

  // mes: si se omite, trae la deuda del año completo en vez de un mes puntual.
  getDeudaPorCategoria(anio: number, mes?: number | null): Observable<CategoriaDeuda[]> {
    const params = mes != null ? `anio=${anio}&mes=${mes}` : `anio=${anio}`;
    return this.http.get<CategoriaDeuda[]>(`${this.apiUrl}/pagos/deuda-por-categoria?${params}`);
  }

  // HU-029: with onlyDebtors the API itself returns just the players that owe something
  getPlayerAccounts(onlyDebtors: boolean): Observable<PlayerAccountModel[]> {
    return this.http.get<PlayerAccountModel[]>(`${this.apiUrl}/pagos/player-accounts?onlyDebtors=${onlyDebtors}`);
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

  registrarPago(idJugador: number, periodo: string, anio: number, monto: number, metodoPago: string): Observable<RegistrarPagoResponse> {
    return this.http.post<RegistrarPagoResponse>(`${this.apiUrl}/pagos/registrar`, {
      idJugador,
      periodo,
      anio,
      monto,
      metodoPago,
    });
  }
}
