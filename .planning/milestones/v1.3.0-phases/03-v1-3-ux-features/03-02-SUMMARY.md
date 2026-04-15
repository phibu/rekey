---
phase: 03-v1-3-ux-features
plan: 02
subsystem: ad-policy-panel
tags: [feat-002, ad-policy, rootdse, memory-cache]
requires: [03-01]
provides:
  - "PasswordPolicy DTO (platform-neutral record in PassReset.Common)"
  - "IPasswordChangeProvider.GetEffectivePasswordPolicyAsync on interface + real + debug providers"
  - "RootDSE-based policy query (minPwdLength, pwdProperties, pwdHistoryLength, minPwdAge, maxPwdAge)"
  - "PasswordPolicyCache wrapping IMemoryCache (1h success / 60s failure TTL, keyed by domain DN)"
  - "GET /api/password/policy returning PolicyResponse (200) or 404 when disabled/unavailable"
  - "ShowAdPasswordPolicy flag on ClientSettings (default false for v1.2.3 parity)"
  - "usePolicy() hook — lazy-fetches and caches policy client-side"
  - "AdPasswordPolicyPanel component rendered above new-password field; fails closed on null"
affects:
  - src/PassReset.Common/PasswordPolicy.cs
  - src/PassReset.Common/IPasswordChangeProvider.cs
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.PasswordProvider/PasswordPolicyCache.cs
  - src/PassReset.Web/Helpers/DebugPasswordChangeProvider.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Models/ClientSettings.cs
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/api/client.ts
  - src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx
key_files_created:
  - src/PassReset.Common/PasswordPolicy.cs
  - src/PassReset.PasswordProvider/PasswordPolicyCache.cs
  - src/PassReset.Web/ClientApp/src/hooks/usePolicy.ts
  - src/PassReset.Web/ClientApp/src/components/AdPasswordPolicyPanel.tsx
key_files_modified:
  - src/PassReset.Common/IPasswordChangeProvider.cs
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.Web/Helpers/DebugPasswordChangeProvider.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Models/ClientSettings.cs
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/api/client.ts
  - src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx
decisions:
  - "Policy cached in-process via IMemoryCache (1h success / 60s failure) keyed by the domain DN rather than per-user — effective policy is domain-wide via default domain policy RootDSE lookup."
  - "Endpoint returns 404 (not empty 200) when disabled or query fails, so the client hook treats absence uniformly and the panel simply hides."
  - "ShowAdPasswordPolicy defaults to false — preserves exact v1.2.3 behavior for operators who do not opt in."
  - "Panel fails closed on null policy: no error toast, no placeholder — just absent. Prevents a flaky AD lookup from surfacing noise to end users."
metrics:
  tasks_completed: 2
  files_created: 4
  files_modified: 9
  completed: "2026-04-15"
  commits:
    - "fbdeb02 feat(03-02): add PasswordPolicy DTO, RootDSE query, and IMemoryCache wrapper"
    - "133a2a4 feat(03-02): wire client policy panel and /api/password/policy endpoint"
deviations:
  - "Plan executed across two sessions — first commit (fbdeb02) landed the DTO + provider + cache; session ended before the client wiring. Second commit (133a2a4) completed the controller endpoint, DI registration, and React panel. No scope changes."
---

# Phase 03 Plan 02: AD Password Policy Panel Summary

## What was built

**Backend (Task 1 — commit fbdeb02):**
- `PasswordPolicy` record in `PassReset.Common` (MinLength, RequiresComplexity, HistoryLength, MinAgeDays, MaxAgeDays).
- `GetEffectivePasswordPolicyAsync` added to `IPasswordChangeProvider`; implemented on `PasswordChangeProvider` (real AD via RootDSE) and `DebugPasswordChangeProvider` (stub for dev).
- `PasswordPolicyCache` sealed class wrapping `IMemoryCache` with 1h success / 60s failure TTL keyed by domain DN.

**Backend (Task 2 — commit 133a2a4):**
- `GET /api/password/policy` endpoint on `PasswordController` — returns `PolicyResponse` (200) or 404 when `ShowAdPasswordPolicy` is false or the AD query yields null.
- `IMemoryCache` + `PasswordPolicyCache` registered in DI (`Program.cs`).
- `ShowAdPasswordPolicy` bool (default `false`) added to `ClientSettings`.

**Frontend (Task 2 — commit 133a2a4):**
- `PolicyResponse` interface + `showAdPasswordPolicy?` on `ClientSettings` (types).
- `fetchPolicy()` in `api/client.ts` (returns `PolicyResponse | null`, swallows 404).
- `usePolicy()` hook — fires only when `showAdPasswordPolicy === true`, caches result.
- `AdPasswordPolicyPanel` component — renders minLength / complexity / history; hidden when policy is null.
- `PasswordForm.tsx` renders panel above the new-password field.

## Verification

- `dotnet build src/PassReset.sln --configuration Release` → 0 errors
- `cd src/PassReset.Web/ClientApp && npm run build` → tsc + Vite green
- `npm test` → existing suite passes (no regressions)
- Manual: `ShowAdPasswordPolicy: false` (default) → UI identical to plan 03-01
- Manual: `ShowAdPasswordPolicy: true` + debug provider → panel renders with stub policy
- Manual: endpoint disabled → 404, panel hidden (fail-closed verified)

## Known limitations

- No automated test yet for `GetEffectivePasswordPolicyAsync` on the real AD provider — covered by manual integration check; proper test lives in a future test-foundation plan.
- Policy cache is per-process; a multi-instance deployment would query AD once per instance (acceptable given the low volume and 1h TTL).
