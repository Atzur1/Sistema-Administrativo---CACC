import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { API_BASE_URL } from './api-url';
import { ChangeRoleRequestModel, EditCandidateRequestModel, EnableUserRequestModel, PortalUserCandidateModel, PortalUserModel, RolPortal } from '../models/PortalUserModel';

@Injectable({
  providedIn: 'root',
})
export class UsuariosPortalService {
  readonly API_URL = `${API_BASE_URL}/usuarios`;

  constructor(private http: HttpClient) {}

  getCandidatos(): Observable<PortalUserCandidateModel[]> {
    return this.http.get<PortalUserCandidateModel[]>(`${this.API_URL}/candidatos`);
  }

  getHabilitados(): Observable<PortalUserModel[]> {
    return this.http.get<PortalUserModel[]>(`${this.API_URL}/habilitados`);
  }

  getDeshabilitados(): Observable<PortalUserModel[]> {
    return this.http.get<PortalUserModel[]>(`${this.API_URL}/deshabilitados`);
  }

  // El SuperAdmin elige el rol con el que vuelve (la pantalla propone el que tenía).
  reactivar(idUsuario: number, rolPortal: RolPortal): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/reactivar`, { rolPortal });
  }

  editarCandidato(idUsuario: number, request: EditCandidateRequestModel): Observable<{ mensaje: string }> {
    return this.http.put<{ mensaje: string }>(`${this.API_URL}/${idUsuario}`, request);
  }

  habilitar(idUsuario: number, request: EnableUserRequestModel): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/habilitar`, request);
  }

  cambiarRol(idUsuario: number, request: ChangeRoleRequestModel): Observable<{ mensaje: string }> {
    return this.http.put<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/rol`, request);
  }

  // El SuperAdmin le manda a una cuenta habilitada el enlace para elegir una contraseña nueva (o para crearla, si
  // todavía no lo había hecho). El enlace llega siempre al correo de esa cuenta.
  resetearContrasena(idUsuario: number): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/resetear-contrasena`, {});
  }

  deshabilitar(idUsuario: number): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${this.API_URL}/${idUsuario}/deshabilitar`, {});
  }
}
