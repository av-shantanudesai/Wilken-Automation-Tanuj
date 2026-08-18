import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from './http-export-api';
import { AuthResponse, AuthUser } from './models';

const AUTH_KEY = 'wilken-auth';
const MOCK_USERS_KEY = 'wilken-mock-users';
const MODE_KEY = 'wilken-api-mode';

interface StoredAuth {
  token: string;
  expiresAt: string;
  user: AuthUser;
}

interface MockAccount {
  id: number;
  email: string;
  displayName: string;
  password: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);

  private readonly session = signal<StoredAuth | null>(this.readSession());

  readonly user = computed(() => this.session()?.user ?? null);
  readonly token = computed(() => this.session()?.token ?? null);
  readonly isLoggedIn = computed(() => {
    const current = this.session();
    if (!current) return false;
    return new Date(current.expiresAt).getTime() > Date.now();
  });

  async register(email: string, password: string, displayName?: string): Promise<void> {
    const response = this.isBackend()
      ? await firstValueFrom(this.http.post<AuthResponse>(`${API_BASE_URL}/auth/register`, {
          email, password, displayName,
        }))
      : this.registerMock(email, password, displayName);
    this.persist(response);
  }

  async login(email: string, password: string): Promise<void> {
    const response = this.isBackend()
      ? await firstValueFrom(this.http.post<AuthResponse>(`${API_BASE_URL}/auth/login`, { email, password }))
      : this.loginMock(email, password);
    this.persist(response);
  }

  logout(redirect = true): void {
    this.session.set(null);
    localStorage.removeItem(AUTH_KEY);
    if (redirect) void this.router.navigateByUrl('/login');
  }

  handleUnauthorized(): void {
    if (!this.session()) return;
    this.logout(true);
  }

  private persist(response: AuthResponse): void {
    const stored: StoredAuth = {
      token: response.token,
      expiresAt: response.expiresAt,
      user: response.user,
    };
    this.session.set(stored);
    localStorage.setItem(AUTH_KEY, JSON.stringify(stored));
  }

  private readSession(): StoredAuth | null {
    try {
      const raw = localStorage.getItem(AUTH_KEY);
      if (!raw) return null;
      const stored = JSON.parse(raw) as StoredAuth;
      if (!stored.token || new Date(stored.expiresAt).getTime() <= Date.now()) {
        localStorage.removeItem(AUTH_KEY);
        return null;
      }
      return stored;
    } catch {
      return null;
    }
  }

  private isBackend(): boolean {
    return (localStorage.getItem(MODE_KEY) || 'mock') === 'backend';
  }

  private registerMock(email: string, password: string, displayName?: string): AuthResponse {
    const normalized = email.trim().toLowerCase();
    if (!normalized.includes('@')) throw new Error('Email is not valid.');
    if (password.length < 8) throw new Error('Password must be at least 8 characters.');

    const users = this.loadMockUsers();
    if (users.some((u) => u.email === normalized)) {
      throw new Error('An account with this email already exists.');
    }

    const user: MockAccount = {
      id: (users.at(-1)?.id ?? 0) + 1,
      email: normalized,
      displayName: (displayName || normalized.split('@')[0]).trim(),
      password,
    };
    users.push(user);
    localStorage.setItem(MOCK_USERS_KEY, JSON.stringify(users));
    return this.toMockResponse(user);
  }

  private loginMock(email: string, password: string): AuthResponse {
    const user = this.loadMockUsers().find((u) => u.email === email.trim().toLowerCase());
    if (!user || user.password !== password) {
      throw new Error('Invalid email or password.');
    }
    return this.toMockResponse(user);
  }

  private toMockResponse(user: MockAccount): AuthResponse {
    const expires = new Date();
    expires.setHours(expires.getHours() + 8);
    return {
      token: `mock.${user.id}`,
      expiresAt: expires.toISOString(),
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
