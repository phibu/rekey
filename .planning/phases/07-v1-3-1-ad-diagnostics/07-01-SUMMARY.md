---
phase: 07-v1-3-1-ad-diagnostics
plan: 01
subsystem: ad-diagnostics
tags: [bug-004, diagnostics, serilog, traceid, exception-chain, log-redaction]
requires: []
provides:
  - "TraceIdEnricherMiddleware — per-request LogContext.PushProperty for Activity.TraceId + SpanId (W3C)"
  - "ExceptionChainLogger static helper — walks InnerException chain for DirectoryServicesCOMException + PasswordException, attaches ExceptionChain[{depth,type,hresult,message}] as structured property"
  - "Nested BeginScope envelope in PasswordController (request scope) and PasswordChangeProvider (flow-step scopes + AD-context scope after FindByIdentity)"
  - "Debug-level step-enter/step-exit events with Stopwatch ElapsedMs for user-lookup, ChangePasswordInternal, Save"
  - "LockoutPasswordChangeProvider Debug events for counter increments + window-eviction sweeps (Warning-level lockout events preserved)"
  - "Targeted PrincipalOperationException catch distinct from generic Exception handler"
  - "ListLogEventSink test infrastructure (handwritten ILogEventSink, zero new package deps)"
  - "ExceptionChainLoggerTests + PasswordLogRedactionTests proving no plaintext password leaks across controller/decorator/provider log surface"
affects:
  - src/PassReset.Web/Middleware/TraceIdEnricherMiddleware.cs
  - src/PassReset.PasswordProvider/ExceptionChainLogger.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs
  - src/PassReset.Tests/Infrastructure/ListLogEventSink.cs
  - src/PassReset.Tests/PasswordProvider/ExceptionChainLoggerTests.cs
  - src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs
key_files_created:
  - src/PassReset.Web/Middleware/TraceIdEnricherMiddleware.cs
  - src/PassReset.PasswordProvider/ExceptionChainLogger.cs
  - src/PassReset.Tests/Infrastructure/ListLogEventSink.cs
  - src/PassReset.Tests/PasswordProvider/ExceptionChainLoggerTests.cs
  - src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs
key_files_modified:
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs
decisions:
  - "D-01: Correlation ID sourced from W3C Activity.TraceId/SpanId (not HttpContext.TraceIdentifier) — CONTEXT.md §1 overrides ROADMAP criterion #4."
  - "D-02: ExceptionChainLogger.LogExceptionChain scoped to DirectoryServicesCOMException + PasswordException only; PrincipalOperationException gets a plain targeted catch with Serilog's default exception destructure."
  - "D-03: Handwritten ListLogEventSink (ILogEventSink) in PassReset.Tests/Infrastructure — no new Serilog test package references, preserves existing dependency graph (NSubstitute + xUnit v3 only)."
  - "D-04: BeginScope at controller (outer) + each provider flow-step (nested). Step-enter/exit as Debug with Stopwatch ElapsedMs; outcome logs remain at Information/Warning."
  - "D-05: AD-context scope opened ONLY inside the if (userPrincipal != null) branch of PerformPasswordChangeAsync — guarantees PrincipalContext.ConnectedServer has bound. Each property null-coalesced to \"unknown\"/\"null\"."
metrics:
  tasks_completed: 7
  files_created: 5
  files_modified: 4
  completed: "2026-04-15"
  commits:
    - "4e7d532 feat(07-01): TraceIdEnricherMiddleware"
    - "3aba1ec feat(07-01): ExceptionChainLogger helper"
    - "367dbdd refactor(07-01): PasswordChangeProvider scopes + catches"
    - "b22c276 refactor(07-01): Lockout decorator Debug events"
    - "deb4d59 feat(07-01): PasswordController request scope"
    - "337a826 test(07-01): ListLogEventSink + ExceptionChainLoggerTests"
    - "a70a5ef test(07-01): PasswordLogRedactionTests (initial commit)"
    - "2561ff7 test(07-01): fix log redaction test setup"
deviations:
  - "Prior executor ran out of turns mid-debug on PasswordLogRedactionTests. Resume executor identified a single failing test (PasswordController_DoesNotLogPlaintext_EndToEnd) whose sink was empty. Root cause: Program.cs wires Serilog via builder.Host.UseSerilog(...), which replaces the ILoggerFactory via a mechanism that bypasses ConfigureTestServices' AddProvider(SerilogLoggerProvider). Fix (2561ff7) replaces ILoggerFactory in the service collection with a SerilogLoggerFactory bound to the capture sink — last registration wins. Test-only change; no product code touched. Covered by pitfall #4 category in the plan (Debug events dropped by filter); distinct in root cause but same end-user symptom (sink empty)."
---

# Phase 07 Plan 01: AD Diagnostics Summary

## What was built

**Middleware & request envelope (Tasks 1, 5 — commits 4e7d532, deb4d59):**
- `TraceIdEnricherMiddleware` pushes W3C `TraceId` + `SpanId` from `Activity.Current` into Serilog's `LogContext` for every request. Registered early in `Program.cs`.
- `PasswordController` opens an outer `BeginScope` per POST request with request metadata; every downstream provider/decorator log inherits the scope.

**Exception diagnostics (Tasks 2, 3 — commits 3aba1ec, 367dbdd):**
- `ExceptionChainLogger.LogExceptionChain` (static helper in `PassReset.PasswordProvider`) walks `InnerException` and attaches a structured `ExceptionChain` array of `{depth, type, hresult, message}` to the log event.
- `PasswordChangeProvider` refactored to open nested scopes (user-lookup, ChangePasswordInternal, Save) with Stopwatch-based `ElapsedMs` Debug step-enter/exit events, and an AD-context scope inside the `userPrincipal != null` branch capturing `Domain`, `DomainController`, `IdentityType`, `UserCannotChangePassword`, `LastPasswordSetUtc`.
- Exception handling split: `DirectoryServicesCOMException` + `PasswordException` invoke `LogExceptionChain`; `PrincipalOperationException` gets a dedicated catch (HResult + message via Serilog's default destructure); generic `Exception` remains as a last-resort fallback.

**Lockout decorator (Task 4 — commit b22c276):**
- `LockoutPasswordChangeProvider` emits Debug events on counter increments and the window-eviction sweep. `ApproachingLockout` / `PortalLockout` Warning events are preserved byte-identical.

**Tests (Tasks 6, 7 — commits 337a826, a70a5ef, 2561ff7):**
- `ListLogEventSink` — handwritten `ILogEventSink` with helpers `AllRendered()` + `AllPropertyValues()`. No new Serilog test package dependencies.
- `ExceptionChainLoggerTests` — asserts chain depth, type names, HResults, and message propagation for nested COM exceptions.
- `PasswordLogRedactionTests` — three sentinel-plaintext coverage tests proving that `SENTINEL_CURRENT_12345` and `SENTINEL_NEW_67890` never appear in rendered messages or property values across:
  1. Minimal provider (surface shape proof)
  2. LockoutPasswordChangeProvider (4 failures crossing Approaching + Portal thresholds)
  3. End-to-end `POST /api/password` via `WebApplicationFactory<Program>` with debug provider

## Verification

- `dotnet build src/PassReset.sln --configuration Release` → 0 errors
- `dotnet test src/PassReset.sln --configuration Release` → **80/80 passed**, 0 failed
- `cd src/PassReset.Web/ClientApp && npm run build` → tsc + Vite green (frontend untouched this plan)
- All 7 plan tasks committed atomically; 8th commit (2561ff7) is the resume-executor test-setup fix for the E2E redaction test.

## Known limitations

- `ListLogEventSink` captures events synchronously; no concurrency stress (acceptable — tests exercise single-request flows).
- End-to-end redaction test runs against the debug provider only (real AD path not reachable in CI). The minimal-provider + lockout-decorator tests cover the code paths that the production AD provider would share in terms of logging surface.
- Log-capture override in `PasswordLogRedactionTests.RedactionFactory` replaces the entire `ILoggerFactory` registration; it intentionally shadows `builder.Host.UseSerilog(...)` rather than extending it, which is the only reliable way to override Program.cs's Serilog config from `WebApplicationFactory` without re-running Program's host-builder chain.

## Self-Check: PASSED

- All 5 created files present on disk.
- All 4 modified files contain the expected changes (confirmed via prior executor's atomic commits).
- All 8 commits present in `git log` (4e7d532, 3aba1ec, 367dbdd, b22c276, deb4d59, 337a826, a70a5ef, 2561ff7).
- Full test suite passes (80/80).
