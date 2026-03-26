import { useEffect, useState } from 'react';
import { fetchSettings } from '../api/client';
import type { ClientSettings } from '../types/settings';

interface UseSettingsResult {
  settings: ClientSettings | null;
  loading: boolean;
  error: string | null;
}

export function useSettings(): UseSettingsResult {
  const [settings, setSettings] = useState<ClientSettings | null>(null);
  const [loading, setLoading]   = useState(true);
  const [error, setError]       = useState<string | null>(null);

  useEffect(() => {
    fetchSettings()
      .then(setSettings)
      .catch((err: unknown) => {
        setError(err instanceof Error ? err.message : 'Failed to load settings');
      })
      .finally(() => setLoading(false));
  }, []);

  return { settings, loading, error };
}
