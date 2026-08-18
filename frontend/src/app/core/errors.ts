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
        return 'Backend not reachable at localhost:5210 (or the request was blocked by CORS) - make sure the API is running, or switch to Mock mode.';
      }
      const detail =
        err.error && typeof err.error === 'object' && 'message' in err.error
          ? String((err.error as { message: unknown }).message)
          : String(err.message ?? '');
      return `Backend error ${err.status}: ${detail}`;
    }
    if ('message' in err) return String(err.message);
  }
  return String(e);
}
