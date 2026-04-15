import { useCallback, useEffect, useRef, useState } from 'react';
import { sha1Hex } from '../utils/sha1';
import { postPwnedCheck } from '../api/client';

/**
 * FEAT-004: possible states of the blur-triggered HIBP breach indicator.
 * - idle: no check in flight and no result to show (empty password)
 * - checking: request queued (debounce in progress) or in flight
 * - safe: HIBP returned a range and the local suffix was NOT found
 * - breached: HIBP returned a range and the local suffix WAS found
 * - unavailable: HIBP proxy unreachable / 503 / network error
 */
export type HibpState = 'idle' | 'checking' | 'safe' | 'breached' | 'unavailable';

/**
 * Debounced HIBP breach check with AbortController-driven cancellation.
 * Plaintext never leaves the browser — only the upper-cased 5-char SHA-1 hex
 * prefix is POSTed to the server. The suffix match is performed locally against
 * the raw HIBP range body proxied by the server.
 */
export function useHibpCheck(debounceMs = 400) {
  const [state, setState] = useState<HibpState>('idle');
  const [count, setCount] = useState(0);
  const abortRef = useRef<AbortController | null>(null);
  const timerRef = useRef<number | undefined>(undefined);

  const check = useCallback(
    (password: string) => {
      // Cancel any in-flight request and any pending debounce timer — the new
      // value supersedes the old. Note that an aborted fetch rejects with
      // AbortError, which we swallow in the catch below.
      abortRef.current?.abort();
      if (timerRef.current !== undefined) {
        window.clearTimeout(timerRef.current);
        timerRef.current = undefined;
      }

      if (!password) {
        setState('idle');
        return;
      }

      setState('checking');

      timerRef.current = window.setTimeout(async () => {
        const ac = new AbortController();
        abortRef.current = ac;
        try {
          const fullHash = (await sha1Hex(password)).toUpperCase();
          const prefix = fullHash.slice(0, 5);
          const suffix = fullHash.slice(5);

          const resp = await postPwnedCheck(prefix, ac.signal);
          if (ac.signal.aborted) return;
          if (!resp || resp.unavailable) {
            setState('unavailable');
            return;
          }

          const match = resp.suffixes
            .split(/\r?\n/)
            .map((l) => l.trim())
            .find((l) => l.toUpperCase().startsWith(suffix + ':'));

          if (match) {
            const parts = match.split(':');
            setCount(parseInt(parts[1] ?? '0', 10) || 0);
            setState('breached');
          } else {
            setCount(0);
            setState('safe');
          }
        } catch (err) {
          if ((err as { name?: string })?.name === 'AbortError') return;
          setState('unavailable');
        }
      }, debounceMs);
    },
    [debounceMs],
  );

  // Cleanup on unmount — abort in-flight request and clear pending timer.
  useEffect(() => {
    return () => {
      abortRef.current?.abort();
      if (timerRef.current !== undefined) {
        window.clearTimeout(timerRef.current);
      }
    };
  }, []);

  return { state, count, check };
}
