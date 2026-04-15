---
status: partial
phase: 07-v1-3-1-ad-diagnostics
source:
  - 07-01-SUMMARY.md
started: 2026-04-15
updated: 2026-04-15
---

## Current Test

[testing skipped by user]

## Tests

### 1. Cold Start Smoke Test
expected: Server boots without errors, GET /api/health returns 200.
result: skipped
reason: User skipped UAT session.

### 2. TraceId appears in request log
expected: Request-completion log line includes a 32-hex-char W3C TraceId.
result: skipped
reason: User skipped UAT session.

### 3. Nested BeginScope envelope properties present
expected: Provider logs inherit controller scope (Username, ClientIp) and add nested FlowStep + AD-context properties.
result: skipped
reason: User skipped UAT session.

### 4. Debug step-enter/step-exit ElapsedMs events
expected: Paired Debug events for user-lookup, ChangePasswordInternal, Save with ElapsedMs > 0.
result: skipped
reason: User skipped UAT session.

### 5. ExceptionChain structured property on COM/PasswordException
expected: Structured ExceptionChain array with depth bound (32) and cycle detection.
result: skipped
reason: User skipped UAT session.

### 6. PrincipalOperationException dedicated catch
expected: Distinct catch path with default Serilog exception destructure.
result: skipped
reason: User skipped UAT session.

### 7. Lockout decorator Debug + Warning events
expected: Debug lockout-counter-incremented + Warning ApproachingLockout/PortalLockout, no duplicates.
result: skipped
reason: User skipped UAT session.

### 8. Password sentinel never appears in logs
expected: SENTINEL_CURRENT_12345 / SENTINEL_NEW_67890 absent from rendered messages and structured properties.
result: skipped
reason: User skipped UAT session.

### 9. Build + tests green after fixes
expected: dotnet build = 0 errors, dotnet test = 81/81 passed.
result: skipped
reason: User skipped UAT session.

## Summary

total: 9
passed: 0
issues: 0
pending: 0
skipped: 9

## Gaps

[none — UAT skipped]
