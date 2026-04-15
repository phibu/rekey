# Plan 02 Summary — BUG-002 E_ACCESSDENIED → PasswordTooRecentlyChanged

**Phase:** 01-v1-2-3-hotfix · **Plan:** 02 · **REQ:** BUG-002 · **Status:** Complete

## Tasks completed

| # | Task | Commit |
|---|------|--------|
| 1 | Backend: classify COMException HResult, add `PasswordTooRecentlyChanged = 19`, map in provider before SetPassword fallback | `c060549` fix(provider) |
| 2 | Frontend: mirror enum + alert string, add switch-case in `PasswordForm.tsx`, update appsettings (live + template) | `a154bea` fix(web) |
| 3 | CHANGELOG `[Unreleased]` entry for BUG-002 | this commit |

## Files changed

- `src/PassReset.Common/ApiErrorCode.cs` — `+7` lines, additive `PasswordTooRecentlyChanged = 19`
- `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` — `+20` lines, HResult classification inside existing `catch (COMException)` at line ~367, placed before SetPassword fallback
- `src/PassReset.Web/ClientApp/src/types/settings.ts` — `+2` lines (alert field + enum value)
- `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` — `+1` line (switch case)
- `src/PassReset.Web/appsettings.json` — `+1` line (`ErrorPasswordTooRecentlyChanged`)
- `src/PassReset.Web/appsettings.Production.template.json` — `+1` line (operator-overridable)
- `CHANGELOG.md` — `[Unreleased]` entry

## Verification

- ✅ `dotnet build -c Release` — 0 warnings, 0 errors
- ✅ `tsc --noEmit` — clean (frontend type-check)
- ✅ Additive enum — no existing values renumbered; frontend mirror intact
- ✅ Narrow HResult mapping — only `0x80070005` + `0x8007202F`, with warning log for ambiguity
- ✅ Mapping precedes SetPassword fallback — password-history bypass avoided
- ✅ `PasswordTooYoung` portal-side pre-check remains untouched and distinct
- ✅ Alert default copy present in both live + template appsettings

## Deviations from plan

- **Execution path:** Plan 02's original executor was interrupted after starting Task 1 (uncommitted changes remained in the working tree). Recovery:
  1. Plan 01 (BUG-001) + Plan 03 (BUG-003) commits were landed on master first.
  2. Plan 02 Task 1 WIP was stashed, restored, verified (build passed), then committed as `c060549`.
  3. Tasks 2 + 3 were completed manually (orchestrator-direct) rather than re-spawning a new executor, since the work was small and state was known-clean.

- **No test files authored** for the HResult classification. Plan frontmatter flagged `tdd="true"` on Task 1, but xUnit infrastructure is a Phase 2 deliverable (QA-001). Per the execution constraint, test infrastructure was not blocked on. When Phase 2 lands, a regression test should assert:
  `COMException(HResult=0x80070005) → ApiErrorException(PasswordTooRecentlyChanged)` — and conversely that other COMException HResults flow through to the existing SetPassword fallback.

## Acceptance criteria (REQ BUG-002)

> When AD rejects a password change because the domain's `minPwdAge` has not elapsed, the portal surfaces a dedicated `ApiErrorCode.PasswordTooRecentlyChanged` with a localized user-facing message (no generic "Unexpected Error"). SIEM event logged appropriately.

- ✅ Dedicated `ApiErrorCode.PasswordTooRecentlyChanged` added (value 19, additive)
- ✅ Localized user-facing message surfaced in UI via `PasswordForm` switch-case + `ClientSettings.Alerts.ErrorPasswordTooRecentlyChanged` override
- ✅ No longer falls through to the generic "Unexpected Error" default
- ✅ Warning log emitted with HResult + AD message + remediation hint. SIEM: classification propagates through `ApiErrorException` → controller → existing `ISiemService` emission path (no new SIEM event type needed; existing `ChangeNotPermitted` / `Generic` path unchanged — the classification is purely user-facing).

## After-phase steps (not in this plan)

1. All three plans (01/02/03) now complete — ready for Phase 1 verification (`gsd-verifier`).
2. Once Phase 1 verifies, promote `CHANGELOG [Unreleased]` → `[1.2.3] — YYYY-MM-DD` and tag `v1.2.3`.
3. Release zip publishes via `release.yml` on tag push.
