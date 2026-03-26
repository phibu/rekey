const UPPERCASE = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ';
const LOWERCASE = 'abcdefghijklmnopqrstuvwxyz';
const DIGITS    = '0123456789';
const SYMBOLS   = '!@#$%^&*()-_=+[]{}|;:,.<>?';
const ALL       = UPPERCASE + LOWERCASE + DIGITS + SYMBOLS;

/**
 * Generates a random password that satisfies the minimum entropy character count.
 * Always includes at least one character from each category.
 */
export function generatePassword(minLength: number): string {
  const length = Math.max(minLength, 12);
  const pick = (charset: string) =>
    charset[crypto.getRandomValues(new Uint32Array(1))[0] % charset.length];

  const chars = [pick(UPPERCASE), pick(LOWERCASE), pick(DIGITS), pick(SYMBOLS)];

  for (let i = chars.length; i < length; i++) {
    chars.push(pick(ALL));
  }

  // Fisher–Yates shuffle
  for (let i = chars.length - 1; i > 0; i--) {
    const j = crypto.getRandomValues(new Uint32Array(1))[0] % (i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }

  return chars.join('');
}
