import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injector, inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthService } from './auth.service';

const AUTH_FREE = ['/auth/login', '/auth/register', '/auth/refresh', '/auth/logout'];

export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const injector = inject(Injector);
  const isApi = req.url.startsWith(environment.apiBaseUrl) || req.url.includes('/api/');
  const isAuthFree = AUTH_FREE.some((path) => req.url.includes(path));

  const auth = injector.get(AuthService);
  const token = auth.token();
  let authorized = isApi ? req.clone({ withCredentials: true }) : req;
  if (token && !token.startsWith('mock.') && isApi && !isAuthFree) {
    authorized = authorized.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
  }

  return next(authorized).pipe(
    catchError((error: unknown) => {
      if (!(error instanceof HttpErrorResponse) || error.status !== 401 || isAuthFree || req.headers.has('X-Silent-Retry')) {
        return throwError(() => error);
      }

      return from(auth.refreshAccessToken()).pipe(
        switchMap((ok) => {
          if (!ok) {
            injector.get(AuthService).handleUnauthorized();
            return throwError(() => error);
          }
          const retryToken = injector.get(AuthService).token();
          if (!retryToken) {
            injector.get(AuthService).handleUnauthorized();
            return throwError(() => error);
          }
          const retry = req.clone({
            withCredentials: true,
            setHeaders: {
              Authorization: `Bearer ${retryToken}`,
              'X-Silent-Retry': '1',
            },
          });
          return next(retry);
        }),
      );
    }),
  );
};
