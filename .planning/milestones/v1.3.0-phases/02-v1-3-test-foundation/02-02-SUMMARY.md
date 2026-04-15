---
phase: 02-v1-3-test-foundation
plan: 02
subsystem: testing
tags: [xunit, dotnet, coverage, refactor, siem, lockout]
requires: ["02-01"]
provides:
  - "Backend xUnit suite covering lockout, HIBP, error-code mapping, syslog formatting, Levenshtein, validation, and PasswordController integration"
  - "PwnedPasswordChecker refactored to instance with injected HttpClient (DI-testable)"
  - "SiemSyslogFormatter extracted as pure static helper from SiemService"
  - "Coverlet.msbuild thresholds enforced — dotnet test fails build when coverage < threshold"
affects:
  - src/PassReset.PasswordProvider/PwnedPasswordChecker.cs
  - src/PassReset.Web/Services/SiemService.cs
  - src/PassReset.Web/Services/SiemSyslogFormatter.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Tests/PassReset.Tests.csproj
key_files_created:
  - src/PassReset.Tests/PasswordProvider/LockoutPasswordChangeProviderTests.cs
  - src/PassReset.Tests/PasswordProvider/PwnedPasswordCheckerTests.cs
  - src/PassReset.Tests/Common/ApiErrorCodeTests.cs
  - src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs
  - src/PassReset.Tests/Web/Helpers/LevenshteinTests.cs
  - src/PassReset.Tests/Web/Models/ChangePasswordModelValidationTests.cs
  - src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs
  - src/PassReset.Tests/Fakes/FakeHttpMessageHandler.cs
  - src/PassReset.Tests/coverage.runsettings
  - src/PassReset.Web/Services/SiemSyslogFormatter.cs
key_files_modified:
  - src/PassReset.PasswordProvider/PwnedPasswordChecker.cs
  - src/PassReset.Web/Services/SiemService.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Tests/PassReset.Tests.csproj
decisions:
  - "Threshold lowered from plan target 55%/45% to 20%/20% line/branch — honest baseline. PasswordChangeProvider AD bindings (largest uncovered surface) need LDAP mocking that is out of scope for this phase."
  - "PasswordControllerTests deserialize through wire-shaped DTOs (ApiResultDto/ApiErrorItemDto) — production ApiResult.Errors and ApiErrorItem.ErrorCode are getter-only and cannot round-trip through System.Text.Json."
  - "Validation-error tests assert on raw response body containing FieldRequired/FieldMismatch — the FromModelStateErrors path serializes a polymorphic shape that the simple DTO cannot capture without a custom converter."
  - "Coverlet.msbuild ThresholdType=line,branch with single Threshold value (20) — multi-value Threshold syntax is rejected by coverlet 8.0.1."
metrics:
  tasks_completed: 3
  files_created: 10
  files_modified: 4
  tests_added: 73
  tests_passing: 74
  coverage_line: 30.13
  coverage_branch: 20.84
  threshold_line: 20
  threshold_branch: 20
  completed: "2026-04-15"
deviations:
  - "Coverage thresholds set to 20%/20% rather than plan's 55%/45%. Threshold *enforcement* is wired (build fails below threshold), but the absolute values reflect what the in-scope test surface achieves. Future phases that mock LDAP can raise these. User explicitly approved this trade-off."
  - "ApiResult and ApiErrorItem in production code remain getter-only. Test DTOs work around this without changing production contracts."
---

# Phase 02 Plan 02: Backend Test Suite Summary

## What was built

- **8 new test files** with 73 new tests covering:
  - `LockoutPasswordChangeProvider` — threshold/window/ApproachingLockout/PortalLockout state transitions
  - `PwnedPasswordChecker` — hash prefix lookup, breach matching, fail-open/closed paths via `FakeHttpMessageHandler`
  - `ApiErrorCode` — numeric value regression guard (BUG-002 watch)
  - `SiemSyslogFormatter` — RFC 5424 message formatting (no sockets)
  - `Levenshtein` — distance helper
  - `ChangePasswordModelValidationTests` — `[Required]`/`[Compare]` annotation behavior
  - `PasswordControllerTests` — `WebApplicationFactory<Program>` integration via in-process `DebugPasswordChangeProvider`

- **3 production refactors** to enable the above:
  - `PwnedPasswordChecker` → instance class with constructor-injected `HttpClient` (was static using `HttpClientFactory`)
  - `SiemSyslogFormatter` extracted as a pure static helper from `SiemService`
  - `Program.cs` already has `public partial class Program {}` from 02-01

- **Coverlet.msbuild thresholds wired** in `PassReset.Tests.csproj` under `Condition="'$(CollectCoverage)' == 'true'"` — `dotnet test src/PassReset.sln -p:CollectCoverage=true` fails the build below 20% line or branch coverage.

## Verification

- `dotnet test src/PassReset.sln` → 74 passed, 0 failed
- `dotnet test src/PassReset.sln -p:CollectCoverage=true` → 30.13% line / 20.84% branch — passes 20/20 gate
- Cobertura report emitted to `src/PassReset.Tests/TestResults/coverage.cobertura.xml`

## Known limitations

- `PasswordChangeProvider` (real AD path) sits at 25% line / 11% branch coverage — uncovered code is `System.DirectoryServices.AccountManagement` calls that need a separate mocking strategy (deferred).
- `Program.cs` excluded from coverage via `ExcludeByFile` — startup wiring is exercised indirectly through `WebApplicationFactory`.
- Two `PasswordControllerTests` validation cases assert on raw response body strings rather than parsed DTOs — `FromModelStateErrors` produces a wrapped error shape that doesn't round-trip cleanly through the simple test DTO.
