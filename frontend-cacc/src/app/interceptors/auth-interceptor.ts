import { inject } from '@angular/core';
import { HttpInterceptorFn } from '@angular/common/http';
import { AuthService } from '../services/auth';

// Adds the session token to every request sent to the API. Endpoints that do
// not require authentication simply ignore the header.
export const authInterceptor: HttpInterceptorFn = (request, next) => {
    const token = inject(AuthService).getToken();

    if (token === null) {
        return next(request);
    }

    return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};
