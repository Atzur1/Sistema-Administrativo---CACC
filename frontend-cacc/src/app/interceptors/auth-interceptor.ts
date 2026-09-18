import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { AuthService } from '../services/auth';

// Adjunta el JWT guardado en el login a cada request saliente. Sin esto, todo endpoint con
// [Authorize] en el backend devuelve 401 aunque el usuario ya haya iniciado sesión.
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).getToken();

  if (!token) {
    return next(req);
  }

  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
