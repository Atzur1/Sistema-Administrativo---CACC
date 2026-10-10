import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth';
import { API_BASE_URL } from '../services/api-url';

// Pedidos a la API: la base es '/api' en desarrollo (proxy) y una URL absoluta en producción
// (environment.prod.ts). Antes se comparaba contra '/api/' fijo, así que en producción el token
// nunca se adjuntaba y toda la API protegida respondía 401. Tampoco se manda a otros orígenes.
function esDeLaApi(url: string): boolean {
  return url === API_BASE_URL || url.startsWith(`${API_BASE_URL}/`);
}

// Login, recuperación y activación funcionan sin sesión (y con una vencida). Logout sí necesita el token.
function esPublica(url: string): boolean {
  return url.startsWith(`${API_BASE_URL}/auth/`) && !url.startsWith(`${API_BASE_URL}/auth/logout`);
}

// Adjunta el JWT guardado en el login a cada request saliente. Sin esto, todo endpoint con
// [Authorize] en el backend devuelve 401 aunque el usuario ya haya iniciado sesión.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.getToken();

  // Public account-access endpoints must remain usable with an expired session
  // and a bad password must not be mistaken for a revoked authenticated session.
  if (!token || !esDeLaApi(req.url) || esPublica(req.url)) {
    return next(req);
  }

  const authenticatedRequest = req.clone({
    setHeaders: { Authorization: `Bearer ${token}` },
  });

  return next(authenticatedRequest).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && authService.isAuthenticated()) {
        authService.logout();
        void router.navigate(['/'], { queryParams: { sesion: 'vencida' } });
      }

      return throwError(() => error);
    }),
  );
};
