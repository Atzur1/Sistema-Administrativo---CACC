import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, CanActivateFn } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from './api-url';

export interface UsuarioLogueado {
  email: string;
  nombre?: string;
  apellido?: string;
  dni?: string;
  rol: number;
  token: string;
}

const SESSION_KEY = 'cacc-session';

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private apiUrl = `${API_BASE_URL}/auth/login`;
  private usuario: UsuarioLogueado | null = null;

  constructor(private http: HttpClient) {
    this.usuario = this.restoreSession();
  }

  login(usuario: string, contrasena: string): Observable<UsuarioLogueado> {
    return this.http.post<UsuarioLogueado>(this.apiUrl, { usuario, contrasena }).pipe(
      tap((respuesta) => {
        this.usuario = respuesta;
        sessionStorage.setItem(SESSION_KEY, JSON.stringify(respuesta));
      }),
    );
  }

  requestPasswordReset(email: string): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${API_BASE_URL}/auth/password-reset/request`, {
      email,
    });
  }

  completeAccountAccess(token: string, password: string): Observable<{ mensaje: string }> {
    return this.http.post<{ mensaje: string }>(`${API_BASE_URL}/auth/password-reset/complete`, { token, password });
  }

  getUsuario(): UsuarioLogueado | null {
    return this.usuario;
  }

  getRol(): number | null {
    return this.getUsuario()?.rol ?? null;
  }

  getToken(): string | null {
    return this.getUsuario()?.token ?? null;
  }

  isAuthenticated(): boolean {
    return this.getUsuario() !== null;
  }

  isAdmin(): boolean {
    return this.getRol() === 1;
  }

  logout() {
    this.usuario = null;
    sessionStorage.removeItem(SESSION_KEY);
  }

  private restoreSession(): UsuarioLogueado | null {
    const raw = sessionStorage.getItem(SESSION_KEY);
    if (!raw) return null;
    try {
      const session = JSON.parse(raw) as UsuarioLogueado;
      const payload = session.token.split('.')[1];
      if (!payload || !session.email || !session.token || !Number.isInteger(session.rol))
        throw new Error('Invalid session');
      const base64 = payload.replace(/-/g, '+').replace(/_/g, '/');
      const claims = JSON.parse(atob(base64.padEnd(Math.ceil(base64.length / 4) * 4, '=')));
      if (!Number.isFinite(claims.exp) || claims.exp * 1000 <= Date.now())
        throw new Error('Expired session');
      return session;
    } catch {
      sessionStorage.removeItem(SESSION_KEY);
      return null;
    }
  }
}

// Exige sesión iniciada, sin importar el rol. Para pantallas que cualquier
// usuario logueado puede ver (ej. "/portales", la elección de entorno) —
// a diferencia de adminGuard, que además exige rol Admin.
export const authGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    alert('Debes iniciar sesión para acceder a esta sección.');
    router.navigate(['/']);
    return false;
  }

  return true;
};

export const adminGuard: CanActivateFn = () => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.isAuthenticated()) {
    alert('Debes iniciar sesión para acceder a esta sección.');
    router.navigate(['/']);
    return false;
  }

  if (!authService.isAdmin()) {
    alert('No tienes permisos para acceder al Portal Administrativo.');
    router.navigate(['/portales']);
    return false;
  }

  return true;
};
