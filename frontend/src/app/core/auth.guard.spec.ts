import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { authGuard } from './auth.guard';
import { AuthService } from './auth.service';

describe('authGuard', () => {
  it('redirects anonymous users to login', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: () => false, tryRefresh: async () => false } },
      ],
    });

    const result = await TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/dashboard' } as never));

    expect(String(result)).toContain('/login');
  });

  it('allows authenticated users', async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: () => true, tryRefresh: async () => false } },
        { provide: Router, useValue: { createUrlTree: () => 'login' } },
      ],
    });

    const result = await TestBed.runInInjectionContext(() =>
      authGuard({} as never, { url: '/dashboard' } as never));

    expect(result).toBe(true);
  });
});
