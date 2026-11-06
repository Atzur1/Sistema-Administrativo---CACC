import { Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';

export type ReportType = 'deudores' | 'pagos-recientes' | 'becados' | 'aranceles' | 'recaudacion';
export interface ExportHistoryItem { id: number; fechaHora: string; usuarioId: number; persona: string; tipo: string; formato: string; filtros: string; cantidadFilas: number; }
export interface ExportHistory { items: ExportHistoryItem[]; total: number; pagina: number; tamanioPagina: number; }

@Injectable({ providedIn: 'root' })
export class ReportesService {
    private readonly API_URL = `${API_BASE_URL}/reportes`;

    constructor(private http: HttpClient) {}

    contar(tipo: ReportType, params: Record<string, string | number> = {}): Observable<{tipo: string; cantidadFilas: number}> {
        return this.http.get<{tipo: string; cantidadFilas: number}>(`${this.API_URL}/${tipo}/conteo`, { params });
    }

    historial(pagina = 1, tamanioPagina = 20): Observable<ExportHistory> {
        return this.http.get<ExportHistory>(`${this.API_URL}/historial`, { params: { pagina, tamanioPagina } });
    }

    // idCategoria: HU-021, respeta el filtro de categoría activo en Deudas y Morosidad.
    exportarDeudoresPdf(idCategoria?: number | null): Observable<HttpResponse<Blob>> {
        return this.getBlob('deudores/pdf', idCategoria != null ? { idCategoria } : {});
    }

    exportarDeudoresCsv(idCategoria?: number | null): Observable<HttpResponse<Blob>> {
        return this.getBlob('deudores/csv', idCategoria != null ? { idCategoria } : {});
    }

    exportarPagosRecientesCsv(): Observable<HttpResponse<Blob>> {
        return this.getBlob('pagos-recientes/csv', { top: 500 });
    }

    exportarPagosRecientesPdf(): Observable<HttpResponse<Blob>> {
        return this.getBlob('pagos-recientes/pdf', { top: 500 });
    }

    exportarBecadosCsv(): Observable<HttpResponse<Blob>> {
        return this.getBlob('becados/csv', {});
    }

    exportarBecadosPdf(): Observable<HttpResponse<Blob>> {
        return this.getBlob('becados/pdf', {});
    }

    exportarArancelesCsv(): Observable<HttpResponse<Blob>> {
        return this.getBlob('aranceles/csv', {});
    }

    exportarArancelesPdf(): Observable<HttpResponse<Blob>> {
        return this.getBlob('aranceles/pdf', {});
    }

    private getBlob(path: string, params: Record<string, string | number>): Observable<HttpResponse<Blob>> {
        const query = Object.entries(params)
            .map(([key, value]) => `${key}=${value}`)
            .join('&');
        const url = query ? `${this.API_URL}/${path}?${query}` : `${this.API_URL}/${path}`;
        return this.http.get(url, { responseType: 'blob', observe: 'response' });
    }
}
