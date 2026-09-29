import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  CambiarRolRequest,
  EditarCandidatoRequest,
  HabilitarUsuarioRequest,
  UsuarioCandidato,
  UsuarioPortal,
} from '../models/UsuarioPortalModel';
import { API_BASE_URL } from './api-url';

@Injectable({
  providedIn: 'root',
})
export class UsuariosPortalService {
  readonly API_URL = `${API_BASE_URL}/usuarios`;

  constructor(private http: HttpClient) {}

  getCandidatos(): Observable<UsuarioCandidato[]> {
    return this.http.get<UsuarioCandidato[]>(`${this.API_URL}/candidatos`);
  }

  getHabilitados(): Observable<UsuarioPortal[]> {
    return this.http.get<UsuarioPortal[]>(`${this.API_URL}/habilitados`);
  }

  editarCandidato(idUsuario: number, request: EditarCandidatoRequest): Observable<{ mensaje: string }> {
    return this.http.put<{ mensaje: string }>(`${this.API_URL}/${idUsuario}`, request);
  }

  habilitar(idUsuario: number, request: HabilitarUsuarioRequest): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/habilitar`, request);
  }

  cambiarRol(idUsuario: number, request: CambiarRolRequest): Observable<{ mensaje: string }> {
    return this.http.put<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/rol`, request);
  }

  deshabilitar(idUsuario: number): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/deshabilitar`, {});
  }
}
