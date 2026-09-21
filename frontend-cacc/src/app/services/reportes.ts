import { Injectable } from '@angular/core';
import { HttpClient, HttpResponse } from '@angular/common/http';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ReportesService {
    private apiUrl = 'http://localhost:5118/api/reportes';

    constructor(private http: HttpClient) {}

    // idCategoria: HU-021, respeta el filtro de categoría activo en Deudas y Morosidad.
    exportarDeudoresPdf(idCategoria?: number | null): Observable<HttpResponse<Blob>> {
        return this.getBlob('deudores/pdf', idCategoria != null ? { idCategoria } : {});
    }

    exportarDeudoresCsv(idCategoria?: number | null): Observable<HttpResponse<Blob>> {
        return this.getBlob('deudores/csv', idCategoria != null ? { idCategoria } : {});
    }

    exportarPagosRecientesCsv(): Observable<HttpResponse<Blob>> {
        return this.getBlob('pagos-recientes/csv', { top: 1000 });
    }

    exportarPagosRecientesPdf(): Observable<HttpResponse<Blob>> {
        return this.getBlob('pagos-recientes/pdf', { top: 1000 });
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
        const url = query ? `${this.apiUrl}/${path}?${query}` : `${this.apiUrl}/${path}`;
        return this.http.get(url, { responseType: 'blob', observe: 'response' });
    }
}
