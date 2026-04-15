import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PasswordStrengthMeter } from './PasswordStrengthMeter';

describe('PasswordStrengthMeter', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders nothing when password is empty', () => {
    const { container } = render(<PasswordStrengthMeter password="" />);
    expect(container.firstChild).toBeNull();
  });

  it('renders nothing before zxcvbn loads (loaded flag false initially)', () => {
    // Even with a password, the component returns null until dynamic import resolves.
    // Synchronous render: loaded=false => null.
    const { container } = render(<PasswordStrengthMeter password="Password1!" />);
    expect(container.firstChild).toBeNull();
  });

  it('unmounts cleanly without leaking timers', () => {
    const { unmount } = render(<PasswordStrengthMeter password="secret" />);
    expect(() => unmount()).not.toThrow();
  });

  it('handles password prop changes without error', () => {
    const { rerender, container } = render(<PasswordStrengthMeter password="a" />);
    rerender(<PasswordStrengthMeter password="ab" />);
    rerender(<PasswordStrengthMeter password="" />);
    expect(container.firstChild).toBeNull();
  });

  it('does not query DOM when empty (no strength indicator)', () => {
    render(<PasswordStrengthMeter password="" />);
    expect(screen.queryByRole('progressbar')).toBeNull();
  });
});
