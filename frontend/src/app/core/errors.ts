import { environment } from '../../environments/environment';

/**
 * Human-readable message from any thrown value, including Angular's
 * HttpErrorResponse (which is not an instanceof Error and would otherwise
 * render as "[object Object]").
 */
export function describeError(e: unknown): string {
  if (e && typeof e === 'object') {
    const err = e as { status?: number; message?: unknown; error?: unknown };
    if (typeof err.status === 'number') {
      if (err.status === 0) {
        return `Backend not reachable at ${environment.apiBaseUrl} (or the request was blocked by CORS) - make sure the API is running, or switch to Mock mode.`;
      }
      if (err.status === 401) return 'Please sign in again.';
      if (err.status === 429) return 'Too many sign-in attempts. Wait a minute and try again.';
      if (err.status === 409) {
        const conflict =
          err.error && typeof err.error === 'object' && 'message' in err.error
            ? String((err.error as { message: unknown }).message)
            : 'This email is already registered.';
        return conflict;
      }
      const detail =
        err.error && typeof err.error === 'object' && 'message' in err.error
          ? String((err.error as { message: unknown }).message)
          : String(err.message ?? '');
      return `Backend error ${err.status}: ${detail}`;
    }
    if ('message' in err) {
      const message = String(err.message);
      if (message.includes('Http failure')) {
        return `Backend not reachable at ${environment.apiBaseUrl} - start the API or switch to Mock mode.`;
      }
      return message;
    }
  }
  return String(e);
}
