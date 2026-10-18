import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { JugadoresSinGeneroModel } from '../models/JugadoresSinGeneroModel';

@Injectable({ providedIn: 'root' })
export class JugadoresSinGeneroService {
  private readonly API_URL = `${API_BASE_URL}/jugadores-sin-genero`;

  constructor(private http: HttpClient) {}

  get(): Observable<JugadoresSinGeneroModel> {
    return this.http.get<JugadoresSinGeneroModel>(this.API_URL);
  }
}
