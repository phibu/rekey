---
phase: 03-v1-3-ux-features
plan: 03
subsystem: clipboard-auto-clear
tags: [feat-003, clipboard, generator, ux]
requires: [03-02]
provides:
  - "ClipboardClearSeconds setting on ClientSettings (default 30, 0 disables)"
  - "scheduleClipboardClear utility with readback guard (only wipes if clipboard content still equals the generated password)"
  - "cancelClipboardClear convenience helper"
  - "ClipboardCountdown chip component (counting / <=5s warning / cleared / idle states, aria-live polite)"
  - "PasswordForm wiring: generator copies password, schedules clear, cancels prior timer on regenerate/submit/unmount"
affects:
  - src/PassReset.Web/Models/ClientSettings.cs
  - src/PassReset.Web/appsettings.json
  - src/PassReset.Web/appsettings.Production.template.json
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/utils/clipboardClear.ts
  - src/PassReset.Web/ClientApp/src/components/ClipboardCountdown.tsx
  - src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx
  - docs/appsettings-Production.md
key_files_created:
  - src/PassReset.Web/ClientApp/src/utils/clipboardClear.ts
  - src/PassReset.Web/ClientApp/src/components/ClipboardCountdown.tsx
key_files_modified:
  - src/PassReset.Web/Models/ClientSettings.cs
  - src/PassReset.Web/appsettings.json
  - src/PassReset.Web/appsettings.Production.template.json
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx
  - docs/appsettings-Production.md
decisions:
  - "Readback guard uses navigator.clipboard.readText() and only writes an empty string when the current clipboard content strictly equals the generated password — guarantees never clobbering anything the user copied between generation and clear."
  - "Seconds <= 0 short-circuits before any timer is created and before any clipboard API is touched — honors the 'disabled = zero side effects' contract."
  - "Clipboard API unavailability (insecure context, older browsers, SSR) is a silent no-op at every layer: scheduleClipboardClear returns a no-op handle, and handleGenerate skips scheduling if writeText is missing or throws."
  - "Regeneration cancels the previous handle before writing the new password; submission also cancels, since a successful submit makes the countdown chip noise."
  - "Cleared state auto-reverts to idle after 2s via a separately-tracked window.setTimeout so it cleans up on unmount alongside the interval handle."
  - "handleGenerate became async (was sync) so the clipboard write can be awaited — error path falls back to idle rather than scheduling a timer that would clear nothing."
metrics:
  tasks_completed: 2
  files_created: 2
  files_modified: 6
  completed: "2026-04-15"
  commits:
    - "791cbdb feat(03-03): add ClipboardClearSeconds config + clipboardClear helper"
    - "ec96345 feat(03-03): wire clipboard auto-clear into PasswordForm"
deviations:
  - "Plan resumed from mid-execution: Task 1 edits were in the working tree (uncommitted) from a prior executor; they were reviewed for soundness against acceptance criteria, then committed as 791cbdb without modification."
  - "handleGenerate signature changed from sync to async to await navigator.clipboard.writeText. No call-sites outside PasswordForm are affected (it is an event handler); onClick tolerates Promise<void> return."
  - "Submit path was extended to also cancel the clipboard timer (not explicitly in plan but matches the 'clear is an aid, not noise' intent — Rule 2 minor hardening, documented for traceability)."
---

# Phase 03 Plan 03: Clipboard Auto-Clear Summary

FEAT-003 one-liner: after the generator copies a password, schedule a `ClipboardClearSeconds` countdown that wipes the clipboard **only if** its content still equals the generated value — so user-copied content mid-countdown is preserved.

## What was built

**`clipboardClear.ts` helper** — exports `scheduleClipboardClear(value, seconds, onTick?, onCleared?, onCancelled?) -> { cancel() }` and `cancelClipboardClear(handle)`. Internals:
- Short-circuits to a no-op handle when `seconds <= 0` or when the Clipboard API is missing.
- Ticks via `setInterval(1s)`; at `remaining <= 0` calls an inner `performClear` that reads the clipboard, compares strict-equals to the captured `value`, and writes `''` only on match. Errors inside the try are swallowed and surface as a silent no-op (matches CONTEXT.md lock decision re: Firefox/Safari readback prompt denial).
- `cancel()` is idempotent via a `cancelled` flag guarding both the interval body and repeat invocations.

**`ClipboardCountdown.tsx` chip** — displays:
- `counting` + `remaining > 5`: default chip with `ContentPasteOffOutlined` icon and `Clipboard clears in Ns`.
- `counting` + `remaining <= 5`: same chip with `color="warning"` (UI-SPEC §Component Inventory #4).
- `cleared`: success chip `Clipboard cleared` with `CheckCircleOutline` — auto-reverts to idle after 2s via PasswordForm's timeout.
- `idle` / `cancelled`: empty but keeps the `aria-live="polite"` region mounted so subsequent announcements are picked up without a re-mount flash.

**`PasswordForm.tsx` wiring** — `handleGenerate` became async: generates, populates both password fields, awaits `navigator.clipboard.writeText(pwd)`, then cancels any prior timer and calls `scheduleClipboardClear`. Handle + cleared-reset timeout tracked via `useRef` and released in a `useEffect` cleanup. `handleSubmit` also cancels the timer because a successful submit supersedes the countdown.

**ClientSettings extension** — C# property + JSON default (30s) in `appsettings.json` and the production template, TypeScript `clipboardClearSeconds?: number` on the `ClientSettings` interface, docs section in `appsettings-Production.md` covering the readback permission prompt on Firefox/Safari and the regeneration-cancels-previous-timer behavior.

## Verification

- `dotnet build src/PassReset.sln --configuration Release` — **0 errors** (pre-existing xUnit1051 warnings only, unrelated).
- `cd src/PassReset.Web/ClientApp && npm run build` — **built in ~184ms, 0 errors**.
- `npm test` — **7 files / 45 tests passed** (no regressions in existing suites).

Behavioral truths from plan frontmatter cross-checked against the implementation:
- Readback guard: `performClear` compares `current === value` before `writeText('')`. ✓
- `ClipboardClearSeconds === 0`: `scheduleClipboardClear` returns `NOOP_HANDLE` before any clipboard access, and `handleGenerate` sets state back to `idle` without scheduling. ✓
- Regeneration cancels previous: `clipboardHandleRef.current?.cancel()` precedes the new `writeText`. ✓
- Clipboard API unavailable: both the helper and `handleGenerate` have guards that silently skip. ✓
- Countdown chip: warning color at `remaining <= 5`; `cleared` chip present for 2s via tracked timeout. ✓

## Known limitations

- **Firefox / Safari readback prompt**: `navigator.clipboard.readText()` may ask the user for permission the first time. Denying it is safe (silent no-op), documented in `appsettings-Production.md`. Chromium permits readback from an active tab without prompting.
- **Insecure contexts**: The Clipboard API is only available over HTTPS (or `localhost`). Operators running plain HTTP will see the chip never appear because `handleGenerate`'s `writeText` guard short-circuits. This is intentional — matches browser policy and CONTEXT.md silent-degrade rule.
- **No automated test for the clear cycle**: The lifecycle is exercised manually (generate → wait → observe clipboard). A Vitest fake-timers test for `scheduleClipboardClear` would strengthen regression coverage; deferred to a future hardening plan since the helper is small and the plan did not mandate unit tests (`tdd="false"`).
- **Chip lives under the new-password TextField**: Per plan action-step 2, placed directly below the generator icon row (outside the InputAdornment). UI-SPEC §Component Inventory #4 accepts this placement.

## Self-Check: PASSED

- `src/PassReset.Web/ClientApp/src/utils/clipboardClear.ts` — **FOUND**
- `src/PassReset.Web/ClientApp/src/components/ClipboardCountdown.tsx` — **FOUND**
- `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` — **FOUND** (modified)
- Commit `791cbdb` — **FOUND**
- Commit `ec96345` — **FOUND**
