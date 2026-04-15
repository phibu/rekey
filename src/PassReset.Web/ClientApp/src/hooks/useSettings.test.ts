import { describe, it, expect, afterEach, vi } from 'vitest';
import { renderHook, waitFor } from '@testing-library/react';
import { mockFetchOnce, mockFetchReject } from '../test-utils/fetchMock';
import { useSettings } from './useSettings';
import type { ClientSettings } from '../types/settings';

const minimalSettings: ClientSettings = {
  usePasswordGeneration: false,
  minimumDistance: 0,
  passwordEntropy: 12,
  showPasswordMeter: false,
  minimumScore: 0,
  useEmail: false,
};

describe('useSettings', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('starts in loading state and resolves with settings on successful fetch', async () => {
    mockFetchOnce(minimalSettings);
    const { result } = renderHook(() => useSettings());

    expect(result.current.loading).toBe(true);
    expect(result.current.settings).toBeNull();
    expect(result.current.error).toBeNull();

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.settings).not.toBeNull();
    expect(result.current.settings?.passwordEntropy).toBe(12);
    expect(result.current.error).toBeNull();
  });

  it('surfaces error message when fetch rejects', async () => {
    mockFetchReject(new Error('boom'));
    const { result } = renderHook(() => useSettings());

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.settings).toBeNull();
    expect(result.current.error).toBe('boom');
  });

  it('surfaces error when fetch returns non-ok status', async () => {
    mockFetchOnce({}, { status: 500 });
    const { result } = renderHook(() => useSettings());

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.settings).toBeNull();
    expect(result.current.error).toMatch(/500/);
  });

  it('surfaces error when response has non-JSON content type', async () => {
    mockFetchOnce('<html>oops</html>', { contentType: 'text/html' });
    const { result } = renderHook(() => useSettings());

    await waitFor(() => expect(result.current.loading).toBe(false));

    expect(result.current.error).toMatch(/Unexpected response format/);
  });
});
