import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, CanActivateFn } from '@angular/router';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from './api-url';

export interface UsuarioLogueado {
  email: string;
  rol: number;
  token: string;
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private apiUrl = `${API_BASE_URL}/auth/login`;
  private usuario: UsuarioLogueado | null = null;

  constructor(private http: HttpClient) {}

  login(usuario: string, contrasena: string): Observable<UsuarioLogueado> {
    return this.http.post<UsuarioLogueado>(this.apiUrl, { usuario, contrasena }).pipe(
      tap((respuesta) => {
        this.usuario = respuesta;
      })
    );
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

