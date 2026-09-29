import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from '../services/auth';

// Adjunta el JWT guardado en el login a cada request saliente. Sin esto, todo endpoint con
// [Authorize] en el backend devuelve 401 aunque el usuario ya haya iniciado sesión.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const token = authService.getToken();

  // Public account-access endpoints must remain usable with an expired session
  // and a bad password must not be mistaken for a revoked authenticated session.
  if (!token || !req.url.startsWith('/api/') || req.url.startsWith('/api/auth/')) {
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
