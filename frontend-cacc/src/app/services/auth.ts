import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Router, CanActivateFn } from '@angular/router';
import { Observable, tap } from 'rxjs';

export interface UsuarioLogueado {
  email: string;
  rol: number;
}

@Injectable({
  providedIn: 'root',
})
export class AuthService {
  private apiUrl = 'http://localhost:5118/api/auth/login';

  constructor(private http: HttpClient) {}

  login(usuario: string, contrasena: string): Observable<UsuarioLogueado> {
    return this.http.post<UsuarioLogueado>(this.apiUrl, { usuario, contrasena }).pipe(
      tap((respuesta) => {
        localStorage.setItem('usuario', JSON.stringify(respuesta));
      })
    );
  }

  getUsuario(): UsuarioLogueado | null {
    const data = localStorage.getItem('usuario');
    return data ? JSON.parse(data) : null;
  }

  getRol(): number | null {
    return this.getUsuario()?.rol ?? null;
  }

  isAuthenticated(): boolean {
    return this.getUsuario() !== null;
  }

  isAdmin(): boolean {
    return this.getRol() === 1;
  }

  logout() {
    localStorage.removeItem('usuario');
  }
}

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

/*/Si el usuario tiene permisos de admin, entra a portales
Si no, muestra un alert('No tienes permisos para acceder a esta sección.')
y lo manda al login con router.navigate(['/'])
Esto cumple lo que pediste sin crear una página extra de “No autorizado”.

Si querés, ahora te ayudo a ponerle el rol real del usuario cuando haga login y que isAdmin() dependa de ese valor concreto./*/