import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PendienteJugador } from './pagos';

export interface PuntoRecaudacionMensual {
  mes: string;
  anio: number;
  monto: number;
}

export interface PuntoCoberturaMensual {
  mes: string;
  anio: number;
  porcentaje: number;
}

export interface ResumenGeneralInfo {
  totalJugadores: number;
  cantidadCategorias: number;
  pagosDelMes: number;
  recaudadoAnioActual: number;
  ingresadoEsteMes: number;
  ingresadoMesAnterior: number;
  deudaAcumulada: number;
  jugadoresSinDeuda: number;
  jugadoresConUnaImpaga: number;
  jugadoresConDosOMasImpagas: number;
  recaudacionMensual: PuntoRecaudacionMensual[];
  coberturaPagoMensual: PuntoCoberturaMensual[];
  mayorDeudaPendiente: PendienteJugador[];
}

@Injectable({ providedIn: 'root' })
export class EstadisticasService {
  private apiUrl = 'http://localhost:5118/api/estadisticas';

  constructor(private http: HttpClient) {}

  getResumenGeneral(): Observable<ResumenGeneralInfo> {
    return this.http.get<ResumenGeneralInfo>(`${this.apiUrl}/resumen-general`);
  }
}
