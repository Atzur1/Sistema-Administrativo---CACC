import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ArancelHistorialItem {
  idArancel: number;
  genero: 'Masculino' | 'Femenino';
  monto: number;
  vigenteDesde: string;
  vigenteHasta: string | null; // null = sigue vigente, todavía no hay uno más nuevo después
  estado: 'Vigente' | 'Programado' | 'Anterior';
}

export interface ArancelResumen {
  arancelMasculinoVigente: number | null;
  arancelFemeninoVigente: number | null;
  proximoCambioFecha: string | null;
}

export interface ProgramarArancelResponse {
  exito: boolean;
  mensaje: string;
}

@Injectable({ providedIn: 'root' })
export class ArancelesService {
  private apiUrl = 'http://localhost:5118/api/aranceles';

  constructor(private http: HttpClient) {}

  getHistorial(): Observable<ArancelHistorialItem[]> {
    return this.http.get<ArancelHistorialItem[]>(`${this.apiUrl}/historial`);
  }

  getResumen(): Observable<ArancelResumen> {
    return this.http.get<ArancelResumen>(`${this.apiUrl}/resumen`);
  }

  programar(genero: string, monto: number, vigenteDesde: string): Observable<ProgramarArancelResponse> {
    return this.http.post<ProgramarArancelResponse>(`${this.apiUrl}/programar`, {
      genero,
      monto,
      vigenteDesde,
    });
  }
}
