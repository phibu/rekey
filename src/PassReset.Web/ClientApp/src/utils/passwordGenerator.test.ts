import { describe, it, expect } from 'vitest';
import { generatePassword } from './passwordGenerator';

const UPPER = /[A-Z]/;
const LOWER = /[a-z]/;
const DIGIT = /[0-9]/;
const SYMBOL = /[!@#$%^&*()\-_=+[\]{}|;:,.<>?]/;

describe('generatePassword', () => {
  it('returns a password of the requested length when >= 12', () => {
    const pwd = generatePassword(16);
    expect(pwd).toHaveLength(16);
  });

  it('enforces minimum length of 12 even when smaller requested', () => {
    const pwd = generatePassword(4);
    expect(pwd).toHaveLength(12);
  });

  it('enforces minimum length of 12 when zero requested', () => {
    const pwd = generatePassword(0);
    expect(pwd).toHaveLength(12);
  });

  it('includes at least one uppercase, lowercase, digit, and symbol', () => {
    for (let i = 0; i < 20; i++) {
      const pwd = generatePassword(12);
      expect(pwd).toMatch(UPPER);
      expect(pwd).toMatch(LOWER);
      expect(pwd).toMatch(DIGIT);
      expect(pwd).toMatch(SYMBOL);
    }
  });

  it('produces high-entropy uniqueness across many iterations', () => {
    const set = new Set<string>();
    for (let i = 0; i < 50; i++) {
      set.add(generatePassword(16));
    }
    // Collisions at length 16 should be astronomically unlikely.
    expect(set.size).toBe(50);
  });

  it('supports longer lengths without truncation', () => {
    const pwd = generatePassword(64);
    expect(pwd).toHaveLength(64);
    expect(pwd).toMatch(UPPER);
    expect(pwd).toMatch(SYMBOL);
  });

  it('contains only characters from the allowed charset', () => {
    const allowed = /^[A-Za-z0-9!@#$%^&*()\-_=+[\]{}|;:,.<>?]+$/;
    for (let i = 0; i < 20; i++) {
      expect(generatePassword(20)).toMatch(allowed);
    }
  });
});
