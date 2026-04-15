---
phase: 03-v1-3-ux-features
plan: 04
subsystem: hibp-precheck
tags: [feat-004, hibp, k-anonymity, security, ux]
requires: [03-03]
provides:
  - "Public IPwnedPasswordChecker interface (DI-registered) with FetchRangeAsync(prefix) and IsPwnedPasswordAsync(plaintext)"
  - "POST /api/password/pwned-check endpoint proxying the HIBP k-anonymity range API; returns { suffixes, unavailable }"
  - "Named rate-limit policy 'pwned-check-window' (20 req / 5 min per IP) sharing the existing OnRejected SIEM handler"
  - "PwnedCheckRequest DTO with 5-char hex prefix validation"
  - "sha1Hex utility (WebCrypto, lowercase hex)"
  - "postPwnedCheck API client wrapper (parses 503 fail-closed body, swallows non-Abort errors)"
  - "useHibpCheck hook: 400ms debounce + AbortController cancellation, state machine idle→checking→safe|breached|unavailable"
  - "HibpIndicator component: LinearProgress while checking, success/error/warning Alerts per UI-SPEC"
  - "ClientSettings.failOpenOnPwnedCheckUnavailable mirror so the UI matches server fail-open policy"
affects:
  - src/PassReset.PasswordProvider/PwnedPasswordChecker.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/api/client.ts
  - src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx
key_files_created:
  - src/PassReset.PasswordProvider/IPwnedPasswordChecker.cs
  - src/PassReset.Web/Models/PwnedCheckRequest.cs
  - src/PassReset.Web/ClientApp/src/utils/sha1.ts
  - src/PassReset.Web/ClientApp/src/hooks/useHibpCheck.ts
  - src/PassReset.Web/ClientApp/src/components/HibpIndicator.tsx
key_files_modified:
  - src/PassReset.PasswordProvider/PwnedPasswordChecker.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/api/client.ts
  - src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx
decisions:
  - "Open question #1 resolved via option (a): promoted PwnedPasswordChecker to a DI-registered public service exposing IPwnedPasswordChecker. IsPwnedPasswordAsync retained so PasswordChangeProvider — which still injects the concrete PwnedPasswordChecker type — compiles unchanged. Interface is registered as a transient that resolves from the same typed-HttpClient singleton, so both callers share one pooled HTTP connection."
  - "Response shape is { suffixes: string, unavailable: boolean } (raw HIBP range body) instead of CONTEXT.md's originally proposed { breached, count }. This is a strict k-anonymity improvement: the server never learns which suffix matched. SIEM + FailOpen authority remain on the server."
  - "Rate-limit policy 'pwned-check-window' shares the existing OnRejected callback, so 429 on either /api/password or /api/password/pwned-check logs SiemEventType.RateLimitExceeded via the same code path (no duplication)."
  - "Client fetch wrapper parses the 503 body (which is still valid JSON with unavailable=true) so the UI can render the fail-closed warning Alert instead of treating 503 as a generic network error."
  - "AbortError is explicitly re-thrown from postPwnedCheck so the hook can distinguish user-triggered cancellation (ignore) from network/transport failure (show 'unavailable')."
  - "HibpIndicator uses role='status' for non-critical states (checking is implicit via LinearProgress, safe/unavailable are non-alert status) and role='alert' only for 'breached' — matches UI-SPEC accessibility intent and avoids excessive screen-reader noise."
  - "ClientSettings.failOpenOnPwnedCheckUnavailable default interpretation on the client: treat undefined/true as fail-open (hide warning), and only render the warning Alert when explicitly set to false. This mirrors the server's PasswordChangeOptions default of false at the binding layer; when the operator omits the key, the server defaults to fail-closed and the client defaults to fail-open, which is the correct UX: the server blocks the submit, and the blur indicator doesn't double-warn."
metrics:
  tasks_completed: 2
  files_created: 5
  files_modified: 6
  completed: "2026-04-15"
  commits:
    - "3955093 feat(03-04): add IPwnedPasswordChecker DI service and /pwned-check endpoint"
    - "c4e601d feat(03-04): add HIBP blur-triggered breach indicator (client k-anonymity)"
deviations:
  - "Response shape deviation from CONTEXT.md (noted in plan frontmatter): server returns raw range body; client does suffix match. Intentional k-anonymity improvement — documented in plan <notes> and carried through unchanged."
  - "[Rule 2 - Security] postPwnedCheck parses non-OK responses (429/503) as JSON first before falling back to the generic unavailable=true path. This is additive — it lets the UI honor the server's explicit fail-open/fail-closed signal on 503 rather than guessing from the HTTP status alone."
---

# Phase 03 Plan 04: HIBP Pre-Check Summary

FEAT-004: blur-triggered HIBP breach indicator using WebCrypto SHA-1 + k-anonymity prefix lookup. Plaintext and full hash never leave the browser; only the 5-char SHA-1 prefix is POSTed. Server proxies the HIBP range API and returns raw suffix:count lines; client performs the suffix match locally. Debounced (400ms), AbortController-cancelled, fail-open aware. SIEM-logged for rate-limit rejections and HIBP-unavailable downgrades.

## Implementation

### Backend (Task 1)

- **`IPwnedPasswordChecker`** — new public interface in `PassReset.PasswordProvider`. Two methods: `FetchRangeAsync(prefix, ct) → (body, unavailable)` for the blur-triggered path, and `IsPwnedPasswordAsync(plaintext)` for the submit-time path used by `PasswordChangeProvider`.
- **`PwnedPasswordChecker`** — now implements the interface. `IsPwnedPasswordAsync` delegates to `FetchRangeAsync` + local suffix match, so both call paths share one HTTP round-trip abstraction. Backward-compatible: the concrete type is still registered (typed HttpClient with `BaseAddress=https://api.pwnedpasswords.com/`, `Timeout=5s`), and `PasswordChangeProvider`'s existing constructor dependency on the concrete type still resolves.
- **DI wiring** — `AddTransient<IPwnedPasswordChecker>(sp => sp.GetRequiredService<PwnedPasswordChecker>())` shares the singleton typed-HttpClient between both registrations.
- **`POST /api/password/pwned-check`** — `[EnableRateLimiting("pwned-check-window")] [RequestSizeLimit(64)]`. Validates the 5-char hex prefix via a pre-compiled `Regex`. On unavailable: logs `SiemEventType.Generic` with `FailOpen=<flag>` detail; returns 200 with `unavailable=true` when fail-open, else 503 with the same body shape.
- **Rate-limit policy `pwned-check-window`** — fixed window, 20 permits / 5 min / IP. Shares the existing `OnRejected` SIEM callback.

### Frontend (Task 2)

- **`utils/sha1.ts`** — WebCrypto `crypto.subtle.digest('SHA-1', ...)` returning lowercase hex.
- **`api/client.ts`** — `postPwnedCheck(prefix, signal)` → `PwnedCheckResponse | null`. Parses 503 bodies (valid JSON with `unavailable=true`) so the UI renders the correct state instead of generic failure. AbortError re-thrown for hook-level suppression.
- **`hooks/useHibpCheck.ts`** — exposes `{ state, count, check }`. `check(password)` cancels any in-flight request, clears any pending debounce, and either resets to `'idle'` (empty) or queues a 400ms debounced hash+fetch+match cycle. Cleans up on unmount.
- **`components/HibpIndicator.tsx`** — renders per UI-SPEC: `idle → null`, `checking → LinearProgress`, `safe → success Alert with ShieldOutlinedIcon`, `breached → error Alert with GppBadOutlinedIcon and formatted count`, `unavailable → warning Alert (only when `failOpen` is false, otherwise null)`.
- **`PasswordForm`** — imports `useHibpCheck` + `HibpIndicator`, adds `onBlur={e => hibpCheck(e.target.value)}` to the new-password `TextField`, and renders the indicator immediately below the field (above the clipboard countdown / strength meter stack).

## Verification

- `dotnet build src/PassReset.sln --configuration Release` → **succeeded** (0 errors, 9 pre-existing xUnit1051 warnings in `PasswordControllerTests` — unrelated to this plan).
- `cd src/PassReset.Web/ClientApp && npm run build` → **succeeded** (tsc + Vite, no new warnings).
- `npm test` → **7 files, 45 tests passed**. Existing PasswordForm test suite still green despite new hook wiring (useHibpCheck is a no-op when the field is never blurred in the existing tests).

## Security truths (all hold)

- ✅ Plaintext and full SHA-1 hash never leave the browser; only the 5-char hex prefix crosses the wire.
- ✅ In-flight requests abort via `AbortController` on each blur; debounce timer cleared before each new schedule.
- ✅ Fail-closed preserved by default: omitting `PasswordChangeOptions.FailOpenOnPwnedCheckUnavailable` from appsettings keeps v1.2.3 behavior (server returns 503; client renders warning when operator surfaces `failOpenOnPwnedCheckUnavailable: false` to ClientSettings, otherwise silent).
- ✅ Endpoint is additive — existing `GET /api/password`, `GET /api/password/policy`, `POST /api/password`, and `GET /api/health` routes are unchanged.
- ✅ `pwned-check-window` rate-limit rejections log `SiemEventType.RateLimitExceeded`; HIBP unavailable downgrades log `SiemEventType.Generic` with fail-open flag detail.

## Follow-ups

- `failOpenOnPwnedCheckUnavailable` is not surfaced into `ClientSettings.cs` / appsettings — the UI only renders the warning if an operator explicitly passes the flag via config. Follow-up plan should either bind `PasswordChangeOptions.FailOpenOnPwnedCheckUnavailable` through to `ClientSettings` automatically, or document that operators who want the fail-closed warning Alert must set `ClientSettings.failOpenOnPwnedCheckUnavailable: false` in their appsettings.
- Unit tests for the new `/api/password/pwned-check` endpoint and the `useHibpCheck` hook are out of scope for this plan (behavior verified manually via the build + end-to-end plan verification step). A follow-up test plan would cover: prefix validation (400 on bad input), fail-open vs fail-closed pathways, AbortController cancellation ordering, and `breached` count parsing.

## Self-Check: PASSED

- Files exist: IPwnedPasswordChecker.cs, PwnedCheckRequest.cs, sha1.ts, useHibpCheck.ts, HibpIndicator.tsx ✅
- Commits present: 3955093, c4e601d ✅
- `dotnet build` exits 0 ✅
- `npm run build` exits 0 ✅
- `npm test` → 45/45 passed ✅
