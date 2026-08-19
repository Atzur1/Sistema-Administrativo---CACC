import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, tap } from 'rxjs';

export interface UsuarioLogueado {
  idUsuario: number;
  email: string;
  idRol: number;
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private apiUrl = 'http://localhost:5118/api/auth/login';

  constructor(private http: HttpClient) {}

  login(email: string, contrasenia: string): Observable<UsuarioLogueado> {
    return this.http.post<UsuarioLogueado>(this.apiUrl, { email, contrasenia }).pipe(
      tap(usuario => {
        localStorage.setItem('usuario', JSON.stringify(usuario));
      })
    );
  }

  getUsuario(): UsuarioLogueado | null {
    const data = localStorage.getItem('usuario');
    return data ? JSON.parse(data) : null;
  }

  getRol(): number | null {
    return this.getUsuario()?.idRol ?? null;
  }

  logout() {
    localStorage.removeItem('usuario');
  }
}