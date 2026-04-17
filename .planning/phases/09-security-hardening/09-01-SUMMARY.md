---
phase: 09-security-hardening
plan: 01
requirements: [STAB-013]
status: complete
commits:
  - df01a51 feat(security): add STAB-013 RedactIfProduction helper for account-enumeration collapse
  - a02635b test(security): add STAB-013 GenericErrorMappingTests with env-based collapse gate
---

# Plan 09-01 Summary — STAB-013

## Outcome

`POST /api/password` now collapses `InvalidCredentials` and `UserNotFound` error
codes to `ApiErrorCode.Generic` (0) on the wire when `IHostEnvironment.IsProduction()`
is true. SIEM emission retains full granularity via the unchanged
`MapErrorCodeToSiemEvent`. Development and Test environments preserve specific
codes for debugging (D-03 regression guard).

## Files Modified

- `src/PassReset.Web/Controllers/PasswordController.cs` — `IsAccountEnumerationCode`
  + `RedactIfProduction` helpers and `IHostEnvironment` constructor injection.
- `src/PassReset.Tests/Web/Controllers/GenericErrorMappingTests.cs` — 4 integration
  tests proving the collapse fires in Production and is absent in Development.
- `src/PassReset.Web/PassReset.Web.csproj` — added `InternalsVisibleTo("PassReset.Tests")`
  so the test project can swap `DebugPasswordChangeProvider` / `NoOpEmailService`
  via `ConfigureTestServices`.

## Verification

- `dotnet test --filter "FullyQualifiedName~GenericErrorMappingTests"` → 4/4 green.
- Production + InvalidCredentials → Generic ✓
- Production + UserNotFound → Generic ✓
- Production + ChangeNotPermitted → code preserved ✓
- Development + InvalidCredentials → code preserved ✓

## Notes

The initial parallel-spawn execution attempt left production-code commits behind
(`df01a51`) but no test commit because the subagent hit a test-wiring issue
(`UseDebugProvider=true` conflicts with `Production` environment guard) and
returned mid-diagnosis. Reconciled inline: reworked the test's factory classes
to use `ConfigureTestServices` + manual provider swap, added `InternalsVisibleTo`
to unblock the compile.

See `tasks/lessons.md` 2026-04-17 entries for the parallel-wave lesson and
stop-pattern lessons that motivated switching to sequential inline execution
for the remainder of phase 9.
