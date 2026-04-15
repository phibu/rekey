import { describe, it, expect } from 'vitest';
import { levenshtein } from './levenshtein';

describe('levenshtein', () => {
  it.each<[string, string, number]>([
    ['', '', 0],
    ['abc', 'abc', 0],
    ['', 'abc', 3],
    ['abc', '', 3],
    ['kitten', 'sitting', 3],
    ['flaw', 'lawn', 2],
    ['Saturday', 'Sunday', 3],
    ['a', 'b', 1],
    ['abc', 'abd', 1],
  ])('distance(%j, %j) === %i', (a, b, expected) => {
    expect(levenshtein(a, b)).toBe(expected);
  });

  it('is case-sensitive', () => {
    expect(levenshtein('abc', 'ABC')).toBe(3);
  });

  it('is symmetric', () => {
    expect(levenshtein('kitten', 'sitting')).toBe(levenshtein('sitting', 'kitten'));
  });

  it('handles single insertion', () => {
    expect(levenshtein('abc', 'abcd')).toBe(1);
  });

  it('handles single deletion', () => {
    expect(levenshtein('abcd', 'abc')).toBe(1);
  });

  it('handles unicode characters', () => {
    // current impl walks code units; test its actual behavior
    expect(levenshtein('café', 'cafe')).toBe(1);
  });
});
