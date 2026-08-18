import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  const isApi = req.url.includes('://localhost:5210/') || req.url.includes('/api/');
  const isAuthCall = req.url.includes('/api/auth/login') || req.url.includes('/api/auth/register');

  const auth = injector.get(AuthService);
  const token = auth.token();

  const authorized = token && !token.startsWith('mock.') && isApi && !isAuthCall
    ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : req;

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !isAuthCall) {
        injector.get(AuthService).handleUnauthorized();
      }
      return throwError(() => error);
    }),
  );
};
