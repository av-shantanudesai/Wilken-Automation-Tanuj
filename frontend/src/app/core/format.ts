/** First 12 characters of a SHA-256 plus an ellipsis; en dash when missing. */
export function shortSha(sha: string | null): string {
  return sha ? `${sha.slice(0, 12)}…` : '–';
}
