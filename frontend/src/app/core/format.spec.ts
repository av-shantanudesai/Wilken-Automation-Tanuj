import { shortSha } from './format';

describe('shortSha', () => {
  it('truncates a full SHA-256 to 12 characters plus an ellipsis', () => {
    const sha = 'a'.repeat(64);
    expect(shortSha(sha)).toBe('aaaaaaaaaaaa…');
  });

  it('renders an en dash for null', () => {
    expect(shortSha(null)).toBe('–');
  });

  it('renders an en dash for the empty string', () => {
    expect(shortSha('')).toBe('–');
  });

  it('keeps inputs shorter than 12 characters intact (plus ellipsis)', () => {
    expect(shortSha('abc123')).toBe('abc123…');
  });
});
