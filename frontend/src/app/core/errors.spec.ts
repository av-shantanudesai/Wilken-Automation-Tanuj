import { describeError } from './errors';
import { environment } from '../../environments/environment';

describe('describeError', () => {
  it('explains status 0 as backend not reachable / CORS', () => {
    expect(describeError({ status: 0 })).toBe(
      `Backend not reachable at ${environment.apiBaseUrl} (or the request was blocked by CORS) - make sure the API is running, or switch to Mock mode.`,
    );
  });

  it('asks the user to sign in again on 401', () => {
    expect(describeError({ status: 401 })).toBe('Please sign in again.');
  });

  it('uses the server-provided message on 409', () => {
    expect(describeError({ status: 409, error: { message: 'Email taken.' } })).toBe('Email taken.');
  });

  it('falls back to the default conflict message on 409 without detail', () => {
    expect(describeError({ status: 409 })).toBe('This email is already registered.');
  });

  it('explains rate limiting on 429', () => {
    expect(describeError({ status: 429 })).toBe(
      'Too many sign-in attempts. Wait a minute and try again.',
    );
  });

  it('reports other statuses with their detail', () => {
    expect(describeError({ status: 500, message: 'boom' })).toBe('Backend error 500: boom');
  });

  it('prefers the response body message for other statuses', () => {
    expect(describeError({ status: 400, error: { message: 'Bad input' }, message: 'ignored' }))
      .toBe('Backend error 400: Bad input');
  });

  it('returns the message of a generic Error', () => {
    expect(describeError(new Error('something broke'))).toBe('something broke');
  });

  it('maps status-less Http failure messages to backend not reachable', () => {
    expect(describeError(new Error('Http failure response for /api/runs: 0 Unknown Error'))).toBe(
      `Backend not reachable at ${environment.apiBaseUrl} - start the API or switch to Mock mode.`,
    );
  });

  it('stringifies plain values', () => {
    expect(describeError('plain failure')).toBe('plain failure');
    expect(describeError(42)).toBe('42');
  });
});
