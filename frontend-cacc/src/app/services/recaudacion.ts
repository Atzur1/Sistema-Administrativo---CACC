import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';

// Un cobro del período, para la tabla "Pagos del período".
export interface PagoDelPeriodo {
  idPago: number;
  jugador: string;
  dni: string;
  categoria: string;
  monto: number;
  medioPago: string; // 'Efectivo' | 'Transferencia'
}

// Lo recaudado en un mes, ya separado por medio de pago (HU-018). Los dos
// subtotales suman el total: la base los calcula sobre las mismas filas.
// Los porcentajes no viajan, los deriva la pantalla.
export interface RecaudacionPeriodo {
  total: number;
  efectivo: number;
  transferencia: number;
  pagos: number;
  pagosEfectivo: number;
  pagosTransferencia: number;
  detalle: PagoDelPeriodo[];
}

@Injectable({ providedIn: 'root' })
export class RecaudacionService {
  private apiUrl = `${API_BASE_URL}/recaudacion`;
  // Los reportes cuelgan de /api/reportes como los del resto del sistema,
  // no del endpoint de datos.
  private reportesUrl = `${API_BASE_URL}/reportes/recaudacion`;

  constructor(private http: HttpClient) {}

  // Un mes sin cobros no es un error: devuelve todo en cero y detalle vacío.
  getDelPeriodo(anio: number, mes: number, idCategoria?: number | null): Observable<RecaudacionPeriodo> {
    let params = new HttpParams().set('anio', anio).set('mes', mes);
    if (idCategoria != null) {
      params = params.set('idCategoria', idCategoria);
    }
    return this.http.get<RecaudacionPeriodo>(this.apiUrl, { params });
  }

  exportarPdf(anio: number, mes: number, idCategoria?: number | null): Observable<HttpResponse<Blob>> {
    return this.descargar('pdf', anio, mes, idCategoria);
  }

  exportarCsv(anio: number, mes: number, idCategoria?: number | null): Observable<HttpResponse<Blob>> {
    return this.descargar('csv', anio, mes, idCategoria);
  }

  // observe: 'response' para poder leer el Content-Disposition y quedarnos con
  // el nombre de archivo que puso el servidor, igual que hace ReportesService.
  private descargar(
    formato: 'pdf' | 'csv', anio: number, mes: number, idCategoria?: number | null,
  ): Observable<HttpResponse<Blob>> {
    let params = new HttpParams().set('anio', anio).set('mes', mes);
    if (idCategoria != null) {
      params = params.set('idCategoria', idCategoria);
    }
    return this.http.get(`${this.reportesUrl}/${formato}`, {
      params,
      responseType: 'blob',
      observe: 'response',
    });
  }
}
