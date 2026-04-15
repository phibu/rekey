import { vi } from 'vitest';

interface MockInit {
  status?: number;
  ok?: boolean;
  contentType?: string;
}

function buildResponse(body: unknown, init: MockInit): Response {
  const status = init.status ?? 200;
  const ok = init.ok ?? (status >= 200 && status < 300);
  const contentType = init.contentType ?? 'application/json';
  return {
    ok,
    status,
    headers: {
      get: (name: string) => (name.toLowerCase() === 'content-type' ? contentType : null),
    },
    json: async () => body,
    text: async () => (typeof body === 'string' ? body : JSON.stringify(body)),
  } as unknown as Response;
}

/**
 * Stubs global fetch to resolve once with the given JSON body.
 * Subsequent calls reject (flushing to any second unmocked call loudly).
 */
export function mockFetchOnce(body: unknown, init: MockInit = {}) {
  const fn = vi.fn().mockResolvedValueOnce(buildResponse(body, init));
  vi.stubGlobal('fetch', fn);
  return fn;
}

/**
 * Stubs global fetch to reject with an error once.
 */
export function mockFetchReject(error: unknown = new Error('network')) {
  const fn = vi.fn().mockRejectedValueOnce(error);
  vi.stubGlobal('fetch', fn);
  return fn;
}

/**
 * Stubs global fetch to resolve with the same response for every call.
 */
export function mockFetchAlways(body: unknown, init: MockInit = {}) {
  const fn = vi.fn().mockResolvedValue(buildResponse(body, init));
  vi.stubGlobal('fetch', fn);
  return fn;
}
