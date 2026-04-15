---
phase: 07-v1-3-1-ad-diagnostics
status: secured
asvs_level: 2
threats_total: 7
threats_closed: 7
threats_open: 0
verified_at: 2026-04-15
verifier: gsd-secure-phase
---

# Phase 07 — AD Diagnostics Security Verification

**Phase:** 07 — v1.3.1 AD Diagnostics (BUG-004)
**ASVS Level:** 2
**Threats Closed:** 7/7
**Status:** SECURED

## Threat Verification Matrix

| Threat ID | Category | Disposition | Verdict | Evidence |
|-----------|----------|-------------|---------|----------|
| T-07-01 | Information Disclosure | mitigate | CLOSED (residual-risk documented) | `src/PassReset.Tests/PasswordProvider/PasswordLogRedactionTests.cs:48-49` (sentinels), `:84-101` (`AssertNoSentinels` flattens rendered + property scalars), `:105-114` (FakeProvider), `:118-140` (LockoutDecorator), `:144-171` (E2E controller via WebApplicationFactory), `:192-211` (ExceptionChainLogger accepted-risk). Class-level XML doc at `:1-46` enumerates uncovered real-provider catch sites and documents mitigation rationale per 07-REVIEW-FIX WR-02. |
| T-07-02 | Information Disclosure | mitigate | CLOSED | `src/PassReset.PasswordProvider/ExceptionChainLogger.cs:67-73` emits only `{depth, type, hresult, message}` — no `Exception.Data`, no `TargetSite`, no `HelpLink`. Top-level exception passed to `LogWarning(exception, ...)` at `:94` for stack-trace-only default destructure. Hardened with `MaxDepth=32` (`:39, :56-60`) and cycle detection via `ReferenceEqualityComparer.Instance` (`:48, :61-65`) per WR-01 fix (commit f9c50ae). |
| T-07-03 | Information Disclosure | accept | CLOSED | `src/PassReset.PasswordProvider/LockoutPasswordChangeProvider.cs` — `currentPassword`/`newPassword` appear only at signature `:83` and passthrough `:104`; verified absent from all `LogWarning` (`:96`, `:115`, `:204`) and `LogDebug` (`:191`, `:221`) templates. T-07-01 LockoutDecorator sentinel test (`PasswordLogRedactionTests.cs:118-140`) provides direct CI evidence. |
| T-07-04 | Tampering | accept | CLOSED | Accepted risk: Serilog rolling File sink writes to `%SystemDrive%\inetpub\logs\PassReset` under standard NTFS ACLs. No filesystem permission changes in scope for phase 07 (CONTEXT.md §Out of Scope). See accepted-risk register below. |
| T-07-05 | Repudiation | mitigate | CLOSED | `src/PassReset.Web/Middleware/TraceIdEnricherMiddleware.cs:38-45` reads `Activity.Current?.TraceId/SpanId` (W3C, not `HttpContext.TraceIdentifier`) and pushes both via nested `using (LogContext.PushProperty(...))` bracketing `await _next(context)`. Registered in `src/PassReset.Web/Program.cs:206`. Controller redundancy at `PasswordController.cs:180-185`. |
| T-07-06 | Denial of Service | accept | CLOSED | Accepted risk: ~12 additional Debug events per request, gated by `Serilog:MinimumLevel:Information` default in `appsettings.Production.json`. Operators opt-in. No retention/sink changes. See accepted-risk register below. |
| T-07-07 | Elevation of Privilege | mitigate | CLOSED | Outer scope owns `Username` at `PasswordController.cs:181-185`. Provider step-envelope Debug templates omit `{Username}` placeholder (verified by 07-VERIFICATION.md row 1 evidence). Exception-path templates intentionally retain `{Username}` for redundancy: `PasswordChangeProvider.cs:174` (PasswordException), `:181` area (PrincipalOperationException), `:485/505/515` (COMException) — Serilog deduplicates by property name so redundant emission is non-shadowing. |

## Accepted Risk Register

| Risk | Threat | Rationale | Owner | Review Date |
|------|--------|-----------|-------|-------------|
| Log files protected by inherited NTFS ACLs only | T-07-04 | Phase 07 is logging-content-only; filesystem hardening is a deployment concern. Default `%SystemDrive%\inetpub\logs\PassReset` ACLs grant Administrators + SYSTEM full control; IIS_IUSRS write-only. Sufficient for ASVS-L2. | ops | v1.4.x |
| Verbose Debug events writable to disk if operator lowers MinimumLevel | T-07-06 | Default `appsettings.Production` keeps `Serilog:MinimumLevel:Information`. Operator opt-in is intentional design. Rolling 30-day / 10MB caps already enforced. | ops | v1.4.x |
| Real `PasswordChangeProvider` catch sites (lines 345/377/386/466/485/505/515) not directly exercised by sentinel tests | T-07-01 (residual) | Driving the real provider requires a live `UserPrincipal` or refactor to swappable AD abstraction — explicitly out of scope for phase 07. Mitigated by (a) code-review confirmation that none of those templates pass plaintext password args, (b) `ExceptionChainLogger_CapturesInnerExceptionMessages_AcceptedRisk` directly exercising the helper those sites invoke, and (c) renamed test names + class-level XML doc making the gap explicit for future maintainers. | dev | v1.4.x |

## Threat Flags from SUMMARY.md

SUMMARY.md does not declare a `## Threat Flags` section. No unregistered flags detected.

## Observability Defects (Non-Security, Informational)

The following Info-level findings from `07-REVIEW.md` are observability defects, not security threats — recorded for transparency:

- **IN-02:** `TraceIdEnricherMiddleware` registered after `UseSerilogRequestLogging()` — request-completion log line emits with `TraceId=unknown`. Does not weaken T-07-05 mitigation for in-flight events; only the post-request summary line is affected. Recommend swap in v1.4.x.
- **IN-01:** `ListLogEventSink.Emit` is not thread-safe (test infrastructure only).
- **IN-03:** Lowercase anonymous-type member names in `ExceptionChain` JSON (schema cosmetic).
- **IN-04:** Controller duplicates `TraceId` already pushed by middleware (Serilog dedupes — harmless).

## Verdict

All 7 threats in the PLAN.md `<threat_model>` register are accounted for. 5 mitigations are present in code with grep-confirmed evidence; 2 accepted risks are documented in the register above. WR-01 (depth-bound + cycle detection) and WR-02 (sentinel-test gap documented + tests renamed) from the deep code review have landed in commits f9c50ae and 12b036c respectively.

**Status: SECURED.**

---

_Verified: 2026-04-15_
_Verifier: gsd-secure-phase_
