import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../environments/environment';
import { AuthResponse, AuthUser } from './models';

const AUTH_KEY = 'wilken-auth';
const MOCK_USERS_KEY = 'wilken-mock-users';
const MODE_KEY = 'wilken-api-mode';
const ACCESS_SKEW_MS = 30_000;

interface StoredAuth {
  token: string;
  expiresAt: string;
  refreshToken: string;
  refreshExpiresAt: string;
  user: AuthUser;
}

interface MockAccount {
  id: number;
  email: string;
  displayName: string;
  passwordHash: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  private readonly session = signal<StoredAuth | null>(this.readSession());
  private refreshInFlight: Promise<boolean> | null = null;
  private refreshTimer: ReturnType<typeof setTimeout> | null = null;

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isLoggedIn = computed(() => this.hasFreshAccess());

  constructor() {
    const current = this.session();
    if (current) this.scheduleRefresh(current.expiresAt);
  }

  async register(email: string, password: string, displayName?: string): Promise<void> {
    const response = this.isBackend()
      ? await firstValueFrom(this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/register`, {
          email, password, displayName,
        }))
      : await this.registerMock(email, password, displayName);
    this.persist(response);
  }

  async login(email: string, password: string): Promise<void> {
    const response = this.isBackend()
      ? await firstValueFrom(this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/login`, { email, password }))
      : await this.loginMock(email, password);
    this.persist(response);
  }

  logout(redirect = true): void {
    this.clearTimer();
    const hadSession = !!this.session();
    this.session.set(null);
    sessionStorage.removeItem(AUTH_KEY);
    if (this.isBackend() && hadSession) {
      void firstValueFrom(this.http.post(`${environment.apiBaseUrl}/auth/logout`, {})).catch(() => undefined);
    }
    if (redirect) void this.router.navigateByUrl('/login');
  }

  handleUnauthorized(): void {
    if (!this.session()) return;
    this.logout(true);
  }

  async tryRefresh(): Promise<boolean> {
    if (this.hasFreshAccess()) return true;
    return this.refreshAccessToken();
  }

  refreshAccessToken(): Promise<boolean> {
    if (this.refreshInFlight) return this.refreshInFlight;
    this.refreshInFlight = this.refreshAccessTokenInternal().finally(() => {
      this.refreshInFlight = null;
    });
    return this.refreshInFlight;
  }

  private async refreshAccessTokenInternal(): Promise<boolean> {
    if (this.isBackend()) {
      try {
        const response = await firstValueFrom(
          this.http.post<AuthResponse>(`${environment.apiBaseUrl}/auth/refresh`, {}),
        );
        this.persist(response);
        return true;
      } catch {
        this.clearTimer();
        this.session.set(null);
        return false;
      }
    }

    const current = this.readSession() ?? this.session();
    if (!current?.refreshToken) return false;
    if (new Date(current.refreshExpiresAt).getTime() <= Date.now()) {
      this.logout(false);
      return false;
    }

    try {
      this.persist(this.refreshMock(current));
      return true;
    } catch {
      this.logout(false);
      return false;
    }
  }

  private persist(response: AuthResponse): void {
    const stored: StoredAuth = {
      token: response.token,
      expiresAt: response.expiresAt,
      refreshToken: response.refreshToken ?? '',
      refreshExpiresAt: response.refreshExpiresAt,
      user: response.user,
    };
    this.session.set(stored);
    if (!this.isBackend()) {
      sessionStorage.setItem(AUTH_KEY, JSON.stringify(stored));
    } else {
      sessionStorage.removeItem(AUTH_KEY);
    }
    this.scheduleRefresh(stored.expiresAt);
  }

  private hasFreshAccess(): boolean {
    const current = this.session();
    if (!current?.token) return false;
    return new Date(current.expiresAt).getTime() - ACCESS_SKEW_MS > Date.now();
  }

  private scheduleRefresh(expiresAt: string): void {
    this.clearTimer();
    const delay = new Date(expiresAt).getTime() - Date.now() - 60_000;
    if (delay <= 0) {
      void this.refreshAccessToken();
      return;
    }
    this.refreshTimer = setTimeout(() => {
      void this.refreshAccessToken();
    }, delay);
  }

  private clearTimer(): void {
    if (this.refreshTimer) {
      clearTimeout(this.refreshTimer);
      this.refreshTimer = null;
    }
  }

  private readSession(): StoredAuth | null {
    if (this.isBackend()) return null;
    try {
      const raw = sessionStorage.getItem(AUTH_KEY);
      if (!raw) return null;
      const stored = JSON.parse(raw) as StoredAuth;
      if (!isStoredAuth(stored)) {
        sessionStorage.removeItem(AUTH_KEY);
        return null;
      }
      if (new Date(stored.refreshExpiresAt).getTime() <= Date.now()) {
        sessionStorage.removeItem(AUTH_KEY);
        return null;
      }
      return stored;
    } catch {
      return null;
    }
  }

  private isBackend(): boolean {
    return (sessionStorage.getItem(MODE_KEY) || localStorage.getItem(MODE_KEY) || 'mock') === 'backend';
  }

  private async registerMock(email: string, password: string, displayName?: string): Promise<AuthResponse> {
    const normalized = email.trim().toLowerCase();
    if (!normalized.includes('@')) throw new Error('Email is not valid.');
    if (password.length < 8 || !/[A-Za-z]/.test(password) || !/\d/.test(password)) {
      throw new Error('Password must be at least 8 characters and contain a letter and a digit.');
    }

    const users = this.loadMockUsers();
    if (users.some((u) => u.email === normalized)) {
      throw new Error('An account with this email already exists.');
    }

    const user: MockAccount = {
      id: (users.at(-1)?.id ?? 0) + 1,
      email: normalized,
      displayName: (displayName || normalized.split('@')[0]).trim(),
      passwordHash: await sha256(password),
    };
    users.push(user);
    localStorage.setItem(MOCK_USERS_KEY, JSON.stringify(users));
    return this.toMockResponse(user);
  }

  private async loginMock(email: string, password: string): Promise<AuthResponse> {
    const hash = await sha256(password);
    const user = this.loadMockUsers().find((u) => u.email === email.trim().toLowerCase());
    const legacyPlain = user && 'password' in user ? String((user as MockAccount & { password?: string }).password ?? '') : '';
    if (!user || (user.passwordHash !== hash && legacyPlain !== password)) {
      throw new Error('Invalid email or password.');
    }
    if (legacyPlain === password) {
      user.passwordHash = hash;
      const users = this.loadMockUsers().map((u) => (u.id === user.id ? user : u));
      localStorage.setItem(MOCK_USERS_KEY, JSON.stringify(users));
    }
    return this.toMockResponse(user);
  }

  private refreshMock(current: StoredAuth): AuthResponse {
    const parts = current.refreshToken.split('.');
    const userId = Number(parts[2] ?? current.user.id);
    if (!Number.isFinite(userId)) throw new Error('Invalid refresh token.');
    return this.toMockResponse({
      id: userId,
      email: current.user.email,
      displayName: current.user.displayName,
      passwordHash: '',
    });
  }

  private toMockResponse(user: MockAccount): AuthResponse {
    const access = new Date();
    access.setMinutes(access.getMinutes() + 15);
    const refresh = new Date();
    refresh.setDate(refresh.getDate() + 7);
    return {
      token: `mock.${user.id}.${Date.now()}`,
      expiresAt: access.toISOString(),
      refreshToken: `mock.refresh.${user.id}.${crypto.randomUUID()}`,
      refreshExpiresAt: refresh.toISOString(),
      user: { id: user.id, email: user.email, displayName: user.displayName },
    };
  }

  private loadMockUsers(): MockAccount[] {
    try {
      return JSON.parse(localStorage.getItem(MOCK_USERS_KEY) || '[]') as MockAccount[];
    } catch {
      return [];
    }
  }
}

function isStoredAuth(value: unknown): value is StoredAuth {
  if (!value || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return typeof v['token'] === 'string'
    && typeof v['expiresAt'] === 'string'
    && typeof v['refreshToken'] === 'string'
    && typeof v['refreshExpiresAt'] === 'string'
    && typeof v['user'] === 'object' && v['user'] !== null;
}

async function sha256(text: string): Promise<string> {
  const data = new TextEncoder().encode(text);
  const hash = await crypto.subtle.digest('SHA-256', data);
  return [...new Uint8Array(hash)].map((b) => b.toString(16).padStart(2, '0')).join('');
}
