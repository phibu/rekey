---
phase: 07-v1-3-1-ad-diagnostics
requirement: BUG-004
milestone: v1.3.1
discussed: 2026-04-15
---

# Phase 07 — AD Diagnostics Context

## Goal

Diagnose intermittent `0x80070005 (E_ACCESSDENIED)` and related password-change failures by adding structured diagnostic logging around every step of the AD password-change flow. External behavior unchanged; user-facing error responses unchanged. No new database or audit dependencies.

## What's Already In Place (don't re-decide)

- **Logging framework:** Serilog via `builder.Host.UseSerilog()` in `Program.cs`, reads `Serilog` section from `appsettings.json`.
- **Sinks:** Console + rolling daily File at `%SystemDrive%\inetpub\logs\PassReset\passreset-.log` (30 days retention, 10 MB per file, shared=true).
- **Enrichment:** `FromLogContext` already wired.
- **Logging primitive:** `ILogger<T>` injected everywhere; structured message templates in use (~30 call sites in `PasswordChangeProvider` alone).
- **Test scaffolding:** xUnit v3 with coverlet; `LockoutPasswordChangeProviderTests` already uses `ILogger` assertions.

## Locked Decisions

### 1. Correlation ID — W3C `Activity.TraceId`

Emit `TraceId` (hex, 32 chars) and `SpanId` (16 chars) from `System.Diagnostics.Activity.Current` on every request-scoped log entry. ASP.NET Core already creates an activity per request; pick it up via a tiny middleware that calls `LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString())` at request entry. Do **not** emit `HttpContext.TraceIdentifier` — the W3C ID is strictly more useful and already flows.

**Why:** zero new infra, forward-compatible with OpenTelemetry if Phase 4 adds distributed telemetry.

### 2. Step-granular logging — `ILogger.BeginScope` envelope per request

For every password-change flow, `PasswordController.PostAsync` opens a single `BeginScope` with `Username`, `TraceId`, and `ClientIp`. Provider methods (`PerformPasswordChangeAsync`, `ValidateGroups`, `SetPassword`) each open their own nested `BeginScope` with the step-specific context. Step-before/after is emitted as **single-line `Debug` events** (`"user-lookup: start"` / `"user-lookup: complete duration={ElapsedMs}"`) so production operators see only `Information`+ unless they flip the level.

**Why:** clean prod output, grep-friendly when diagnostics are on, no duplicate properties sprayed across 6+ messages per request.

### 3. Exception chain — custom walker for the two rich exception types

For `DirectoryServicesCOMException` and `PasswordException`, log via a helper `LogExceptionChain(ILogger, Exception)` that walks `InnerException` and emits a structured property `ExceptionChain` as an array of objects:

```json
[
  { "depth": 0, "type": "DirectoryServicesCOMException", "hresult": "0x80070005", "message": "Access is denied." },
  { "depth": 1, "type": "COMException", "hresult": "0x80070005", "message": "..." }
]
```

For all other exceptions, pass the exception object to the existing `ILogger.LogWarning(ex, …)` / `LogError(ex, …)` calls — Serilog's default destructure is sufficient.

**Why:** HResult + depth is the diagnostic signal we need for E_ACCESSDENIED; everything else is fine with default destructure.

### 4. AD context — once per request via scope

After the user principal is resolved in `PasswordChangeProvider.PerformPasswordChangeAsync`, open a `BeginScope` with these properties (never inside the scope — only from the returned principal):

- `Domain` (e.g. `contoso.local`)
- `DomainController` (the DC hostname that answered — from `PrincipalContext.ConnectedServer`)
- `IdentityType` (e.g. `SamAccountName`)
- `UserCannotChangePassword` (bool)
- `LastPasswordSetUtc` (ISO 8601 or `null`)

Every log entry emitted below that scope inherits these properties automatically. Success and failure paths both get the context.

**Why:** single write, all downstream lines annotated, matches the scope-based approach in #2.

### 5. Redaction safety net — test-based

Add an xUnit v3 test `PasswordLogRedactionTests` that:

1. Spins up a test `ILogger<T>` via a `ListLogger` / `TestSink` that captures every rendered message + property bag.
2. Drives `PasswordChangeProvider.PerformPasswordChangeAsync` (debug path via `DebugPasswordChangeProvider` where AD isn't reachable) with known sentinel plaintext passwords (e.g. `"SENTINEL_CURRENT_12345"`, `"SENTINEL_NEW_67890"`).
3. Asserts no rendered message or property value contains either sentinel string.
4. Repeated for `LockoutPasswordChangeProvider` and `PasswordController.PostAsync` (via `WebApplicationFactory`).

No runtime filter — too brittle, easily circumvented by a casual template change.

**Why:** catches the constraint regression at build time, in CI, without any production overhead.

## Lockout Decorator State-Transition Logging

Add `Debug` events in `LockoutPasswordChangeProvider` for:
- Counter increment (`"lockout counter {Count}/{Threshold} for {Username}"`)
- Threshold crossed → `ApproachingLockout` (`Warning`, already logged; keep)
- Threshold exceeded → `PortalLockout` (`Warning`, already logged; keep)
- Window eviction (the 5-minute sweep) — new `Debug` event: `"evicted {Count} expired lockout entries"`

## Files Likely Touched

- `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` (primary)
- `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs`
- `src/PassReset.PasswordProvider/ExceptionChainLogger.cs` (NEW — helper)
- `src/PassReset.Web/Controllers/PasswordController.cs`
- `src/PassReset.Web/Program.cs` (add TraceId middleware)
- `src/PassReset.Web/Middleware/TraceIdEnricherMiddleware.cs` (NEW — tiny)
- `src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs` (NEW)
- `src/PassReset.Tests/PasswordProvider/ExceptionChainLoggerTests.cs` (NEW)

## Out of Scope (deferred)

- OpenTelemetry / distributed-tracing exporters — Phase 4 (v2.0 Multi-OS PoC) will revisit.
- Aggregation service integration (Seq, Elastic, Azure Monitor) — operator choice, not packaged.
- Metrics/counters (Prometheus-style) — separate concern.
- Changing Serilog sinks, file paths, or retention — already correct.
- User-facing error response changes — explicit non-goal per BUG-004.

## Success Criteria (reiterated from ROADMAP)

1. Every AD password-change call path logs structured events for user lookup (before/after), `ChangePasswordInternal` (before/after), and `Save()` (before/after) — including AD context captured once per request via scope.
2. Exceptions on `DirectoryServicesCOMException` and `PasswordException` paths emit structured `ExceptionChain` arrays with type, HResult, message, and depth.
3. Every request correlates via W3C `Activity.TraceId`, emitted as a log-context property on every entry during the request.
4. Lockout decorator logs state transitions (counter increments, threshold crossings already exist, window evictions new).
5. No passwords or plaintext ever appear in log output — verified by `PasswordLogRedactionTests`.
6. User-facing error responses unchanged from v1.3.0.
