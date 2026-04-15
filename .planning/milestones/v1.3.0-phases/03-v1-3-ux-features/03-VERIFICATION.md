---
phase: 03-v1-3-ux-features
verified: 2026-04-15T12:50:00Z
status: human_needed
score: 4/4 plans verified (all must-haves pass; human UAT recommended for UX truths)
overall_verdict: PASS
plan_verdicts:
  "03-01": PASS
  "03-02": PASS
  "03-03": PASS
  "03-04": PASS
builds:
  dotnet: "0 errors, 9 pre-existing xUnit1051 warnings (unrelated to phase 03)"
  frontend_build: "tsc + Vite green, chunk-size warning only"
  frontend_tests: "7 files / 45 tests passed (0 failed)"
human_verification:
  - test: "Set Branding.LogoFileName and drop asset into C:\\ProgramData\\PassReset\\brand\\; load portal"
    expected: "BrandHeader shows logo; favicon changes; helpdesk block renders; omitting Branding returns v1.2.3 look"
    why_human: "Visual rendering, runtime favicon injection, image onError fallback — DOM-mutation effects"
  - test: "Enable ShowAdPasswordPolicy and load portal against debug provider"
    expected: "AdPasswordPolicyPanel renders minLength=12, complexity, history=24; 404 when disabled hides panel"
    why_human: "Real AD RootDSE query requires domain controller; cache TTL behavior (1h/60s) needs timing"
  - test: "Generate password, wait ClipboardClearSeconds, inspect clipboard; also copy other text mid-countdown"
    expected: "Clipboard cleared only if content still equals generated password; chip transitions counting→cleared; regenerate resets timer"
    why_human: "Browser clipboard permission prompt (Firefox/Safari), readText availability, insecure-context no-op"
  - test: "Type password, blur, inspect DevTools Network"
    expected: "Request body contains only 5-char SHA-1 prefix; rapid blur aborts prior requests; breached/safe/unavailable Alerts per UI-SPEC"
    why_human: "Debounce timing (400ms), AbortController cancellation, live HIBP API call"
---

# Phase 03 (v1.3 UX Features) — Verification Report

**Phase goal:** Deliver four operator/user-facing UX features (branding, AD policy panel, clipboard auto-clear, HIBP pre-check) on the v1.2.3 base, shipped as v1.3.0, with no breaking appsettings changes.

**Overall verdict: PASS** — all four plans delivered their must-haves end-to-end. Code exists, is substantive, is wired into entry points, builds clean on both backends, and all 45 frontend tests pass. Human UAT recommended for visual/UX truths but no automated gaps block the phase.

## Commit trail

| Plan | Commits |
|------|---------|
| 03-01 | d1a589b BrandingSettings + /brand route + installer, 3a83211 BrandHeader + favicon + helpdesk, 74fc49e docs |
| 03-02 | fbdeb02 DTO + RootDSE + cache, 133a2a4 client panel + endpoint, a0db559 docs |
| 03-03 | 791cbdb helper + config, ec96345 PasswordForm wiring, 89b13eb docs |
| 03-04 | 3955093 IPwnedPasswordChecker + endpoint, c4e601d client k-anonymity indicator, 445a4a1 docs |

## Per-plan verification

### 03-01 Operator Branding (FEAT-001) — PASS

| Must-have truth | Status | Evidence |
|---|---|---|
| 8 branding fields on ClientSettings.Branding | VERIFIED | `BrandingSettings` class in `Models/ClientSettings.cs` with all 8 nullable props |
| /brand/* served from C:\ProgramData\PassReset\brand\ | VERIFIED | `Program.cs:239-246` — `PhysicalFileProvider` + `RequestPath = "/brand"` |
| Installer creates brand dir, upgrade-safe | VERIFIED | `Install-PassReset.ps1:186-193` — Test-Path guard, "Preserving existing" branch |
| Omitting Branding preserves v1.2.3 look | VERIFIED | `BrandHeader.tsx` falls back to `LockPersonIcon` + "PassReset" |
| Favicon runtime injection | VERIFIED | `App.tsx` useEffect updates `<link rel="icon">` href |
| Helpdesk block (URL + mailto) | VERIFIED | `App.tsx` renders links with `target=_blank rel=noopener` and `mailto:` |

### 03-02 AD Password Policy Panel (FEAT-002) — PASS

| Must-have truth | Status | Evidence |
|---|---|---|
| Panel shows min length, complexity, history | VERIFIED | `AdPasswordPolicyPanel.tsx` dense List with CheckCircleOutline rows |
| Fails closed on AD error | VERIFIED | `usePolicy.ts` → null on 404; panel returns `null` when policy is null |
| 1h success / 60s failure TTL | VERIFIED | `PasswordPolicyCache.cs` — `TimeSpan.FromHours(1)` / `FromSeconds(60)` |
| GET /api/password/policy | VERIFIED | `PasswordController.cs:81` — `[HttpGet("policy")]` returns 404 when disabled or null |
| GetEffectivePasswordPolicyAsync on interface + both impls | VERIFIED | Real provider reads RootDSE attrs; debug returns `(12, true, 24, 1, 90)` |
| IMemoryCache + cache registered in DI | VERIFIED | `Program.cs:147-148` — `AddMemoryCache` + `AddSingleton<PasswordPolicyCache>` |

### 03-03 Clipboard Auto-Clear (FEAT-003) — PASS

| Must-have truth | Status | Evidence |
|---|---|---|
| Readback guard: only clear on strict match | VERIFIED | `clipboardClear.ts` — `if (current === value) writeText('')` |
| ClipboardClearSeconds=0 short-circuits | VERIFIED | `clipboardClear.ts` returns NOOP_HANDLE before any clipboard access |
| Regenerate cancels prior timer | VERIFIED | `PasswordForm.tsx:187,238` — `clipboardHandleRef.current?.cancel()` before rescheduling |
| API unavailable → silent no-op | VERIFIED | Helper guards `navigator.clipboard`/`readText`/`writeText` presence |
| Countdown chip states (counting/warning/cleared) | VERIFIED | `ClipboardCountdown.tsx` — color="warning" at ≤5s, success chip on cleared, aria-live polite |

### 03-04 HIBP Pre-Check (FEAT-004) — PASS

| Must-have truth | Status | Evidence |
|---|---|---|
| Blur indicator with debounce 400ms | VERIFIED | `useHibpCheck.ts` — `setTimeout(..., debounceMs)` with 400 default |
| Client SHA-1 via WebCrypto; only 5-char prefix sent | VERIFIED | `sha1.ts` uses `crypto.subtle.digest('SHA-1',...)`; hook slices `fullHash.slice(0,5)` |
| Server proxies HIBP range, returns { suffixes, unavailable } | VERIFIED | `PasswordController.cs:101` endpoint + `PwnedPasswordChecker.FetchRangeAsync` |
| Rate-limit policy pwned-check-window 20/5min | VERIFIED | `Program.cs:153` + `[EnableRateLimiting("pwned-check-window")]` on endpoint |
| AbortController on value change | VERIFIED | `useHibpCheck.ts` — `abortRef.current?.abort()` before new schedule |
| Omitting FailOpen preserves v1.2.3 fail-closed | VERIFIED | Server returns 503 when flag absent; client Alert hidden unless flag explicitly false |

**Note:** Response-shape deviation from CONTEXT.md (`{breached,count}` → `{suffixes,unavailable}`) is documented in 03-04 SUMMARY and is a strict k-anonymity improvement.

## Build / Test results

- `dotnet build src/PassReset.sln --configuration Release` → **0 errors**, 9 pre-existing xUnit1051 warnings (phase-02 test files, unrelated)
- `npm run build` → **green** (tsc + Vite 167ms; only chunk-size info warning)
- `npm test` → **45/45 passed, 7 files**

## Anti-patterns / gaps found

- **None blocking.** No TODO/FIXME/placeholder stubs in the new artifacts. All new components are wired into `PasswordForm.tsx` / `App.tsx`. No orphaned files.
- **Minor follow-up noted in 03-04 SUMMARY:** `failOpenOnPwnedCheckUnavailable` not auto-surfaced from `PasswordChangeOptions` to `ClientSettings`; operator must set it explicitly if they want the warning Alert. Documented, not a phase-03 gap.
- **Known testing gap (acknowledged in SUMMARYs):** No Vitest unit tests yet for `scheduleClipboardClear`, `useHibpCheck`, `usePolicy`, or favicon useEffect. Behavior verified manually + via end-to-end build. A follow-up hardening test plan could strengthen regression coverage.

## Human verification required

See `human_verification` block in frontmatter — 4 UAT items covering visual rendering, browser clipboard behavior, DevTools network inspection, and live AD RootDSE queries.

---

_Verified: 2026-04-15 — Claude (gsd-verifier)_
