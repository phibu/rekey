import type { ApiResult, ChangePasswordRequest, ClientSettings } from '../types/settings';

export async function fetchSettings(): Promise<ClientSettings> {
  const res = await fetch('/api/password');
  if (!res.ok) throw new Error(`Failed to load settings: ${res.status}`);
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

  const data: ApiResult = await res.json();
  return data;
}
