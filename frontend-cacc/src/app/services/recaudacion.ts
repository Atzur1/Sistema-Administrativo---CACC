import { Injectable } from '@angular/core';
import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { CollectionPeriodModel } from '../models/CollectionPeriodModel';

@Injectable({ providedIn: 'root' })
export class RecaudacionService {
  private readonly API_URL = `${API_BASE_URL}/recaudacion`;
  // Los reportes cuelgan de /api/reportes como los del resto del sistema,
  // no del endpoint de datos.
  private readonly REPORTS_URL = `${API_BASE_URL}/reportes/recaudacion`;

  constructor(private http: HttpClient) {}

  // Un mes sin cobros no es un error: devuelve todo en cero y detalle vacío.
  getDelPeriodo(anio: number, mes: number, idCategoria?: number | null): Observable<CollectionPeriodModel> {
    let params = new HttpParams().set('anio', anio).set('mes', mes);
    if (idCategoria != null) {
      params = params.set('idCategoria', idCategoria);
    }
    return this.http.get<CollectionPeriodModel>(this.API_URL, { params });
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
    return this.http.get(`${this.REPORTS_URL}/${formato}`, {
      params,
      responseType: 'blob',
      observe: 'response',
    });
  }
}
