import { sanitizeReturnUrl } from './login';

describe('sanitizeReturnUrl', () => {
  it('allows internal paths', () => {
    expect(sanitizeReturnUrl('/jobs')).toBe('/jobs');
    expect(sanitizeReturnUrl('/audit?runId=RUN-1')).toBe('/audit?runId=RUN-1');
  });

  it('falls back to /dashboard when missing', () => {
    expect(sanitizeReturnUrl(null)).toBe('/dashboard');
    expect(sanitizeReturnUrl('')).toBe('/dashboard');
  });

  it('rejects absolute URLs', () => {
    expect(sanitizeReturnUrl('https://evil.example/phish')).toBe('/dashboard');
    expect(sanitizeReturnUrl('javascript:alert(1)')).toBe('/dashboard');
  });

  it('rejects protocol-relative URLs', () => {
    expect(sanitizeReturnUrl('//evil.example')).toBe('/dashboard');
  });

  it('rejects backslash-based escapes', () => {
    expect(sanitizeReturnUrl('/\\evil.example')).toBe('/dashboard');
  });
});
