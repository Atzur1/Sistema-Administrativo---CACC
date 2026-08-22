import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface UsuarioLogueado {
  email: string;
  rol: number;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = 'http://localhost:5118/api/auth/login';

  constructor(private http: HttpClient) {}

  login(usuario: string, contrasena: string): Observable<UsuarioLogueado> {
    return this.http.post<UsuarioLogueado>(this.apiUrl, { usuario, contrasena }).pipe(
      tap(respuesta => {
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

  logout() {
    localStorage.removeItem('usuario');
  }
}