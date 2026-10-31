import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import {
  ScheduleTariffResponseModel,
  TariffChangeRequest,
  TariffHistoryItemModel,
  TariffImpactModel,
  TariffSummaryModel,
} from '../models/TariffModel';

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

  // HU-106: cuántos jugadores y cuánto dinero alcanza el cambio, calculado por el backend sin grabar nada.
  simular(cambio: TariffChangeRequest): Observable<TariffImpactModel> {
    return this.http.post<TariffImpactModel>(`${this.API_URL}/simular`, cambio);
  }

  // Confirma el cambio simulado. huella: la del resumen que se revisó; jugadoresConfirmados: la cantidad que se
  // escribió para confirmar (si el cambio alcanza más de una categoría). Si el impacto ya no es el revisado, el
  // backend no graba y responde 409 con el impacto actual en error.impacto.
  programar(cambio: TariffChangeRequest, huella: string, jugadoresConfirmados: number | null): Observable<ScheduleTariffResponseModel> {
    return this.http.post<ScheduleTariffResponseModel>(`${this.API_URL}/programar`, {
      ...cambio,
      huella,
      jugadoresConfirmados,
    });
  }
}
