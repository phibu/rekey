import type {
  ApiResult,
  ChangePasswordRequest,
  ClientSettings,
  PolicyResponse,
  PwnedCheckResponse,
} from '../types/settings';

export async function fetchSettings(): Promise<ClientSettings> {
  const res = await fetch('/api/password');
  if (!res.ok) throw new Error(`Failed to load settings: ${res.status}`);
  const contentType = res.headers.get('content-type') ?? '';
  if (!contentType.includes('application/json'))
    throw new Error('Unexpected response format from settings endpoint');
  return res.json() as Promise<ClientSettings>;
}

export async function changePassword(request: ChangePasswordRequest): Promise<ApiResult> {
  const res = await fetch('/api/password', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });

  if (res.status === 429) {
    return { errors: [{ errorCode: 15 }] };
  }

  const contentType = res.headers.get('content-type') ?? '';
  if (!contentType.includes('application/json')) {
    return { errors: [{ errorCode: 0, message: 'An unexpected server error occurred.' }] };
  }

  const data: ApiResult = await res.json();
  return data;
}

// FEAT-002: returns null on 404 (disabled or AD failure) so callers can hide the panel.
export async function fetchPolicy(): Promise<PolicyResponse | null> {
  try {
    const res = await fetch('/api/password/policy');
    if (!res.ok) return null;
    return (await res.json()) as PolicyResponse;
  } catch {
    return null;
  }
}

// FEAT-004: POST the 5-char SHA-1 prefix and receive the raw HIBP range body.
// Returns null on network error. A 503 response still parses as a valid
// PwnedCheckResponse with unavailable=true so the caller can render the
// fail-closed warning state.
export async function postPwnedCheck(
  prefix: string,
  signal?: AbortSignal,
): Promise<PwnedCheckResponse | null> {
  try {
    const res = await fetch('/api/password/pwned-check', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prefix }),
      signal,
    });

    // 429 / 5xx / any other non-OK: try to parse the body (the 503 fail-closed
    // path returns a valid JSON PwnedCheckResponse). If parsing fails, treat as
    // unavailable so the indicator falls through to the warning/hidden state.
    if (!res.ok) {
      try {
        const body = (await res.json()) as PwnedCheckResponse;
        return { suffixes: body.suffixes ?? '', unavailable: true };
      } catch {
        return { suffixes: '', unavailable: true };
      }
    }

    return (await res.json()) as PwnedCheckResponse;
  } catch (err) {
    // AbortError propagates so the hook can distinguish cancellation from failure.
    if ((err as { name?: string })?.name === 'AbortError') throw err;
    return null;
  }
}
