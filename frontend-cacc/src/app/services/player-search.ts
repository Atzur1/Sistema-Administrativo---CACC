import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { PlayerSearchResultModel } from '../models/PlayerSearchResultModel';
import { API_BASE_URL } from './api-url';

// HU-023: búsqueda de alumnos por DNI o apellido del lado del servidor.
@Injectable({ providedIn: 'root' })
export class PlayerSearchService {
  // Mismo mínimo que valida PlayersController.SearchPlayers.
  static readonly MIN_TERM_LENGTH = 2;
  // Tope de resultados de la API (PlayersController.MaxSearchResults).
  static readonly MAX_RESULTS = 50;

  constructor(private http: HttpClient) {}

  search(term: string): Observable<PlayerSearchResultModel[]> {
    return this.http.get<PlayerSearchResultModel[]>(`${API_BASE_URL}/players/search`, {
      params: new HttpParams().set('term', term),
    });
  }
}
