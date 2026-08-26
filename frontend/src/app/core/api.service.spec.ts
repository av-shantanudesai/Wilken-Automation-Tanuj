import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { ApiService } from './api.service';
import { MockExportApi } from './mock-export-api';

describe('ApiService', () => {
  beforeEach(() => {
    localStorage.removeItem('wilken-api-mode');
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideRouter([{ path: 'login', children: [] }]),
        MockExportApi,
        ApiService,
      ],
    });
  });

  it('defaults to mock in development when useMock is enabled', () => {
    const api = TestBed.inject(ApiService);
    expect(api.allowMock).toBe(true);
    expect(api.mode()).toBe('mock');
  });

  it('can switch to backend mode', () => {
    const api = TestBed.inject(ApiService);
    api.setMode('backend');
    expect(api.mode()).toBe('backend');
    expect(localStorage.getItem('wilken-api-mode')).toBe('backend');
  });
});
