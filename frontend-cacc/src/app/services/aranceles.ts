import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';

// Un arancel es por género O por categoría: el que no aplica viene null. Si la categoría de un
// jugador tiene arancel propio, ese gana; si no, se usa el de su género.
export interface ArancelHistorialItem {
  idArancel: number;
  genero: 'Masculino' | 'Femenino' | null;
  idCategoria: number | null;
  nombreCategoria: string | null;
  monto: number;
  vigenteDesde: string;
  vigenteHasta: string | null; // null = sigue vigente, todavía no hay uno más nuevo después
  estado: 'Vigente' | 'Programado' | 'Anterior';
  // Solo se cancelan los aranceles del mes en curso en adelante; los anteriores son historial.
  puedeCancelar: boolean;
  responsableNombre?: string | null;
  responsableApellido?: string | null;
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
  private apiUrl = `${API_BASE_URL}/aranceles`;

  constructor(private http: HttpClient) {}

  getHistorial(): Observable<ArancelHistorialItem[]> {
    return this.http.get<ArancelHistorialItem[]>(`${this.apiUrl}/historial`);
  }

  getResumen(): Observable<ArancelResumen> {
    return this.http.get<ArancelResumen>(`${this.apiUrl}/resumen`);
  }

  // Quita el arancel y las cuotas pendientes sin pagos emitidas con él. El backend lo rechaza (400, con
  // el motivo en "mensaje") si alguna de esas cuotas ya tiene pagos o si el arancel es de un mes anterior.
  cancelar(idArancel: number): Observable<ProgramarArancelResponse> {
    return this.http.delete<ProgramarArancelResponse>(`${this.apiUrl}/${idArancel}`);
  }

  // destino: un género ('Masculino' | 'Femenino') o una categoría ({ idCategoria }), uno solo.
  programar(
    destino: { genero: string } | { idCategoria: number },
    monto: number,
    vigenteDesde: string,
  ): Observable<ProgramarArancelResponse> {
    return this.http.post<ProgramarArancelResponse>(`${this.apiUrl}/programar`, {
      ...destino,
      monto,
      vigenteDesde,
    });
  }
}
