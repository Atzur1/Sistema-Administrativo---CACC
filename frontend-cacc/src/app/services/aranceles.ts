import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { ScheduleTariffResponseModel, TariffHistoryItemModel, TariffSummaryModel } from '../models/TariffModel';

@Injectable({ providedIn: 'root' })
export class ArancelesService {
  private readonly API_URL = `${API_BASE_URL}/aranceles`;

  constructor(private http: HttpClient) {}

  getHistorial(): Observable<TariffHistoryItemModel[]> {
    return this.http.get<TariffHistoryItemModel[]>(`${this.API_URL}/historial`);
  }

  getResumen(): Observable<TariffSummaryModel> {
    return this.http.get<TariffSummaryModel>(`${this.API_URL}/resumen`);
  }

  // Quita el arancel y las cuotas pendientes sin pagos emitidas con él. Responde 204 sin cuerpo. El backend
  // lo rechaza (400, con el motivo en "mensaje") si alguna de esas cuotas ya tiene pagos o si el arancel es
  // de un mes anterior.
  cancelar(idArancel: number): Observable<void> {
    return this.http.delete<void>(`${this.API_URL}/${idArancel}`);
  }

  // destino: un género ('Masculino' | 'Femenino') o una categoría ({ idCategoria }), uno solo.
  programar(
    destino: { genero: string } | { idCategoria: number },
    monto: number,
    vigenteDesde: string,
  ): Observable<ScheduleTariffResponseModel> {
    return this.http.post<ScheduleTariffResponseModel>(`${this.API_URL}/programar`, {
      ...destino,
      monto,
      vigenteDesde,
    });
  }
}
