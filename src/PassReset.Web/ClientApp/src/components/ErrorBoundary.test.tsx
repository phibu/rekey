import { describe, it, expect, afterEach, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ErrorBoundary } from './ErrorBoundary';

function Boom(): JSX.Element {
  throw new Error('kaboom');
}

describe('ErrorBoundary', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('renders children when no error is thrown', () => {
    render(
      <ErrorBoundary>
        <div>happy path</div>
      </ErrorBoundary>,
    );
    expect(screen.getByText('happy path')).toBeInTheDocument();
  });

  it('renders fallback UI when a child throws', () => {
    // Silence React's console.error output for the expected error
    const errSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    render(
      <ErrorBoundary>
        <Boom />
      </ErrorBoundary>,
    );
    expect(screen.getByText(/Something went wrong/i)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /reload page/i })).toBeInTheDocument();
    errSpy.mockRestore();
  });

  it('fallback reload button triggers window.location.reload', () => {
    const errSpy = vi.spyOn(console, 'error').mockImplementation(() => {});
    const reload = vi.fn();
    // jsdom's window.location.reload is non-configurable in some versions — override defensively
    Object.defineProperty(window, 'location', {
      configurable: true,
      value: { ...window.location, reload },
    });

    render(
      <ErrorBoundary>
        <Boom />
      </ErrorBoundary>,
    );
    screen.getByRole('button', { name: /reload page/i }).click();
    expect(reload).toHaveBeenCalledTimes(1);
    errSpy.mockRestore();
  });
});
