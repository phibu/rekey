---
phase: 09-security-hardening
reviewed: 2026-04-17T00:00:00Z
depth: standard
files_reviewed: 15
files_reviewed_list:
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Controllers/PasswordController.cs
  - src/PassReset.Web/Services/AuditEvent.cs
  - src/PassReset.Web/Services/ISiemService.cs
  - src/PassReset.Web/Services/SiemService.cs
  - src/PassReset.Web/Services/SiemSyslogFormatter.cs
  - src/PassReset.Web/Models/SiemSettings.cs
  - src/PassReset.Web/Models/SiemSettingsValidator.cs
  - src/PassReset.Web/PassReset.Web.csproj
  - src/PassReset.Tests/Web/Controllers/GenericErrorMappingTests.cs
  - src/PassReset.Tests/Web/Controllers/RateLimitAndRecaptchaTests.cs
  - src/PassReset.Tests/Web/Startup/HttpsRedirectionTests.cs
  - src/PassReset.Tests/Web/Services/AuditEventRedactionTests.cs
  - src/PassReset.Tests/Web/Services/SiemSyslogFormatterTests.cs
  - src/PassReset.Tests/Web/Startup/EnvironmentVariableOverrideTests.cs
findings:
  critical: 0
  warning: 4
  info: 7
  total: 11
status: issues_found
---

# Phase 9: Code Review Report — Security Hardening

**Reviewed:** 2026-04-17
**Depth:** standard
**Files Reviewed:** 15 (9 production, 6 test)
**Status:** issues_found (no CRITICAL — 4 HIGH/WARNING, 7 MEDIUM/LOW)

## Executive Summary

| Severity | Count | Release Gate |
|----------|-------|--------------|
| CRITICAL | 0 | — |
| HIGH (Warning) | 4 | Should fix before v1.4.0 |
| MEDIUM (Info) | 5 | Triage / backlog |
| LOW (Info) | 2 | Backlog |

No secret leakage, no compile-breakers, no hot-path throw paths introduced. Each of the five STABs (013/014/015/016/017) lands as specified in the locked decisions (D-01 through D-20). The issues below are mostly **consistency and isolation gaps** rather than security defects:

1. A legacy (`EmitSyslog`) code path still uses a hard-coded SD-ID `PassReset@0` — STAB-015's configurable SD-ID only applies to the new `AuditEvent` overload. SIEM pipelines consuming both paths will see two different structured-data identifiers.
2. The STAB-013 test suite exercises the controller via `IPasswordChangeProvider` but does **not** prove the SIEM granularity guarantee (D-05) — SIEM events for `InvalidCredentials` vs `UserNotFound` are not asserted anywhere in the new test coverage.
3. `EnvironmentVariableOverrideTests` modifies process-level environment variables without a mutex; xUnit v3 runs test classes in parallel by default, so another test class that reads `SmtpSettings__Password` could observe the leaked value.
4. The security-headers middleware in `Program.cs` resolves `IOptions<WebSettings>` **per request** from the request scope — low-risk but avoidable given `IOptions<T>` is a singleton.

---

## Warnings (HIGH)

### WR-01: Legacy syslog path emits hard-coded SD-ID `PassReset@0`, bypassing `SiemSettings.SdId`

**File:** [src/PassReset.Web/Services/SiemSyslogFormatter.cs:29-30](src/PassReset.Web/Services/SiemSyslogFormatter.cs#L29)
**Issue:**
`SiemSyslogFormatter.Format(... eventType, username, ipAddress, detail)` — the pre-STAB-015 overload that is still called from `SiemService.EmitSyslog()` — hard-codes the SD-ID as `PassReset@0`. The new STAB-015 configurable `SdId` from `SiemSettings.Syslog.SdId` is only wired through the `AuditEvent` overload. Because `PasswordController.Audit()` still calls `_siemService.LogEvent(eventType, username, ip, detail)` (the non-structured overload — see `PasswordController.cs:242`), every SIEM event currently emitted on the hot path uses the hard-coded `PassReset@0` SD-ID, and **none** use the configured `passreset@32473` default or any operator override. This means:
- The STAB-015 operator escape hatch (D-20) is non-functional for all existing event types.
- SIEM parsers keyed on `SdId` will see two different identifiers depending on whether future code migrates to the `AuditEvent` overload.
- `SiemSettings.Syslog.SdId` is effectively dead config until a caller adopts the structured overload.

**Additionally**, the hard-coded `@0` is not a valid IANA-reserved PEN — RFC 5424 §6.3.2 requires either IANA-registered names or `name@<PEN>` where PEN is a real/placeholder private enterprise number. `@32473` (IANA-reserved example) is the correct placeholder the configurable path already uses.

**Evidence:**
```csharp
// Line 29-30 — hardcoded SD-ID; no SdId parameter
return $"<{priority}>1 {ts} {hostname} {appName} - - - " +
       $"[PassReset@0 event=\"{eventType}\" user=\"{EscapeSd(username)}\" ip=\"{EscapeSd(ipAddress)}\"{detailPart}]";
```
Caller `PasswordController.Audit()` at line 242 uses the legacy overload, not `AuditEvent`:
```csharp
_siemService.LogEvent(siemEvent.Value, username, clientIp, detail);
```

**Fix:**
Either (a) migrate `PasswordController.Audit()` to construct an `AuditEvent` and call the new overload (preferred — closes STAB-015 as-designed), or (b) extend the legacy overload to accept `sdId` and have `SiemService.EmitSyslog` pass `_settings.Syslog.SdId`. Option (a):
```csharp
// PasswordController.cs — Audit()
if (siemEvent.HasValue)
{
    _siemService.LogEvent(new AuditEvent(
        EventType: siemEvent.Value,
        Outcome:   outcome,
        Username:  username,
        ClientIp:  clientIp,
        TraceId:   System.Diagnostics.Activity.Current?.TraceId.ToString(),
        Detail:    detail));
}
```
Then remove the legacy `EmitSyslog` path (or keep it only for callers that have no `AuditEvent`).

---

### WR-02: STAB-013 tests do not prove SIEM granularity guarantee (D-05)

**File:** [src/PassReset.Tests/Web/Controllers/GenericErrorMappingTests.cs:1-220](src/PassReset.Tests/Web/Controllers/GenericErrorMappingTests.cs#L1)
**Issue:**
D-05 locks the invariant that SIEM continues to emit `SiemEventType.InvalidCredentials` vs `SiemEventType.UserNotFound` granularity **while** the wire response collapses to `Generic`. The four tests in this file only assert the wire shape. A regression that accidentally collapses both wire AND SIEM (for example, passing the redacted `wireError` into `MapErrorCodeToSiemEvent`) would pass all four tests.

**Evidence:**
The test class docstring explicitly calls this out: "SIEM granularity is NOT covered here" (line 26-27), but there is no companion test file that does cover it. The validation map (09-VALIDATION.md row STAB-013 SIEM granularity) also flags this as "⚠ may need new unit test class" — still unaddressed in Wave 0.

**Fix:**
Add a test that asserts `ISiemService.LogEvent` is invoked with `SiemEventType.InvalidCredentials` for the `invalidCredentials` magic user AND with `SiemEventType.UserNotFound` for the `userNotFound` magic user, both in the Production factory. Use a test double for `ISiemService` that captures invocations:
```csharp
public sealed class CapturingSiemService : ISiemService
{
    public List<(SiemEventType, string, string, string?)> Calls { get; } = new();
    public List<AuditEvent> StructuredCalls { get; } = new();
    public void LogEvent(SiemEventType t, string u, string ip, string? d = null) => Calls.Add((t, u, ip, d));
    public void LogEvent(AuditEvent e) => StructuredCalls.Add(e);
}

// In ProductionEnvFactory.ConfigureTestServices:
services.RemoveAll<ISiemService>();
services.AddSingleton<ISiemService, CapturingSiemService>();

// Test body:
var siem = (CapturingSiemService)factory.Services.GetRequiredService<ISiemService>();
Assert.Contains(siem.Calls, c => c.Item1 == SiemEventType.InvalidCredentials);
```

---

### WR-03: `EnvironmentVariableOverrideTests` mutates process env vars without cross-class isolation

**File:** [src/PassReset.Tests/Web/Startup/EnvironmentVariableOverrideTests.cs:27-38](src/PassReset.Tests/Web/Startup/EnvironmentVariableOverrideTests.cs#L27)
**Issue:**
xUnit v3 runs tests **within a class sequentially** but **across classes in parallel** by default (the prompt flags this). `SetEnv` writes to `EnvironmentVariableTarget.Process`, which is a shared mutable global. While `Dispose` nulls the variables for the current class, any concurrently-executing test class that reads `SmtpSettings__Password` (e.g., a future test for `SmtpEmailService`) can observe the leaked value for the duration of the facts in this class. The same hazard applies to `ClientSettings__Recaptcha__PrivateKey`.

Additionally, if a test body throws **before** `Dispose()` runs (or if the CLR is terminated mid-test), the env var persists into the next test process on the same build agent — CI flake potential. xUnit's `IDisposable.Dispose()` runs per test, which limits the blast radius, but the **concurrent** hazard remains unresolved.

**Evidence:**
```csharp
// Line 27-31
private void SetEnv(string name, string? value)
{
    Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
    _envVarsSet.Add(name);
}
```
No `[Collection]` attribute and no parallelization opt-out.

**Fix:**
Either opt out of cross-class parallelization for env-var-mutating tests, or use a collection fixture to serialize:
```csharp
[CollectionDefinition("EnvVarSerial", DisableParallelization = true)]
public class EnvVarSerialCollection { }

[Collection("EnvVarSerial")]
public class EnvironmentVariableOverrideTests : IDisposable { /* ... */ }
```
Recommend the `DisableParallelization = true` flag over a whole-suite `[Collection]`-less approach; it scopes the serialization cost to this one class without slowing other suites.

---

### WR-04: `SiemService` `Dispose()` not thread-safe with concurrent `SendTcp`/`SendUdp`

**File:** [src/PassReset.Web/Services/SiemService.cs:51-56](src/PassReset.Web/Services/SiemService.cs#L51)
**Issue:**
`Dispose()` tears down `_tcpStream`, `_tcpClient`, `_udpClient` without taking `_syslogLock`. A request on a different thread can be mid-`SendUdp` / `SendTcp` when the DI container disposes the singleton at app shutdown, producing an `ObjectDisposedException` inside the `try` block. Because the catch (lines 107-110, 140-143) logs and swallows, it wouldn't crash the process — but it would emit spurious error-level log lines during graceful shutdown and, more importantly, the `TcpClient` reset path (lines 175-182) could race with `Dispose` and leave a dangling stream.

This is a pre-existing issue amplified (not introduced) by Phase 9 because the new `EmitSyslogStructured` adds a second caller of `SendTcp`/`SendUdp`.

**Evidence:**
```csharp
// Line 51-56 — no lock on dispose
public void Dispose()
{
    _tcpStream?.Dispose();
    _tcpClient?.Dispose();
    _udpClient?.Dispose();
}
```

**Fix:**
Take `_syslogLock` in `Dispose` and set fields to null so subsequent `SendTcp`/`SendUdp` rebuilds or returns early:
```csharp
public void Dispose()
{
    lock (_syslogLock)
    {
        _tcpStream?.Dispose(); _tcpStream = null;
        _tcpClient?.Dispose(); _tcpClient = null;
        _udpClient?.Dispose(); _udpClient = null;
    }
}
```
Low risk: Dispose runs once at shutdown; contention window is microseconds.

---

## Info (MEDIUM)

### IN-01: Security-headers middleware resolves `IOptions<WebSettings>` per request from request scope

**File:** [src/PassReset.Web/Program.cs:240](src/PassReset.Web/Program.cs#L240)
**Issue:**
`context.RequestServices.GetRequiredService<IOptions<WebSettings>>()` runs on every request. `IOptions<T>` is a singleton, so this is functionally correct — but going through the request scope's resolver incurs a dictionary lookup per request that is trivially eliminated by capturing the singleton in the outer closure. It also raises a minor semantic concern: if someone later migrates `WebSettings` to `IOptionsMonitor<T>` or `IOptionsSnapshot<T>`, the current call pattern obscures which contract is expected.

Performance-wise this is negligible (this is explicitly out of v1 review scope), but clarity-wise: document **why** it's per-request (D-12/D-14 escape hatch reload? — no, `IOptions<T>` is fixed-snapshot so it is not) or hoist it out of the hot path.

**Evidence:**
```csharp
// Line 240 — resolves from request scope each call
var runtimeWeb = context.RequestServices.GetRequiredService<IOptions<WebSettings>>().Value;
```
Compare with `_clientSettings.Value` in controllers (captured once in ctor).

**Fix:**
Hoist to the outer scope and close over the value:
```csharp
var webSettingsAccessor = app.Services.GetRequiredService<IOptions<WebSettings>>();
app.Use(async (context, next) =>
{
    var runtimeWeb = webSettingsAccessor.Value;
    // ...
});
```
Or: since `webSettings` is already captured at line 91-93, reuse that snapshot directly (it's the same `IOptions<T>.Value` output).

**Thread-safety note (addresses prompt focus 4):** `IOptions<T>.Value` returns the same instance each call; no mutation, no races. The per-request resolve is safe — just wasteful.

---

### IN-02: `PasswordController.Audit()` calls legacy `LogEvent` — does not populate `TraceId` in SIEM

**File:** [src/PassReset.Web/Controllers/PasswordController.cs:234-243](src/PassReset.Web/Controllers/PasswordController.cs#L234)
**Issue:**
`PostAsync` already computes `traceId` at line 184 for the logging scope, but `Audit()` discards it — it only forwards `eventType`, `username`, `clientIp`, `detail` to the legacy `SiemService.LogEvent`. STAB-015's `AuditEvent` has a `TraceId` field for exactly this correlation use case, and `SiemSyslogFormatter.Format(... evt)` emits `traceId="..."` when non-null. Operators cannot correlate SIEM events with application logs via trace ID until the controller adopts the `AuditEvent` overload.

**Evidence:**
`Audit()` signature at line 234 has no `traceId` param; it's computed at line 184 but never flows to SIEM.

**Fix:** Same as WR-01's Option (a) — migrate `Audit()` to build an `AuditEvent` that includes the trace ID.

---

### IN-03: reCAPTCHA test secrets are Google's public test keys (documented) — pin the source comment

**File:** [src/PassReset.Tests/Web/Controllers/RateLimitAndRecaptchaTests.cs:100-106](src/PassReset.Tests/Web/Controllers/RateLimitAndRecaptchaTests.cs#L100)
**Issue:**
The `SiteKey` / `PrivateKey` at lines 105-106 are Google's documented public test keys (`6LeIxAcTAAAAAJcZVRqyHh71UMIEGNQ_MXjiZKhI`). The comment at line 100 references the URL, which is good. However:
1. These keys **always pass** (the docs say any token verifies successfully) — the test expects `InvalidCaptcha` when using token `"ThisTokenWillNotVerify"`, which contradicts Google's documented behavior for the test key. The test may be currently passing only because the `score` check at `PasswordController.cs:297` (`json.Score >= config.ScoreThreshold`) fails — but the test doesn't assert *why* verification failed.
2. This creates a **silent dependency** on Google returning a specific score. If Google changes the test-key score to 1.0, the test inverts.

**Evidence:** Per Google's docs (https://developers.google.com/recaptcha/docs/faq), the keys listed always return `success=true`. The action check at line 298 of PasswordController (`action == "change_password"`) is a more likely source of the failure for this test.

**Fix:** Add a comment clarifying *which* branch of `ValidateRecaptchaAsync` is expected to reject this request, and assert the SIEM event type is `RecaptchaFailed` (which the test currently does not verify — it only asserts wire `InvalidCaptcha`).

---

### IN-04: `SiemSettingsValidator.SdId` character allowlist check is a denylist, not an allowlist

**File:** [src/PassReset.Web/Models/SiemSettingsValidator.cs:44-52](src/PassReset.Web/Models/SiemSettingsValidator.cs#L44)
**Issue:**
RFC 5424 §6.3.2 defines `SD-NAME` as `PRINTUSASCII` minus `"=", SP, "]", %d34 (")`. The validator uses `IndexOfAny([' ', '=', ']', '"']) >= 0` which catches the four forbidden characters — good — but does NOT enforce the **PRINTUSASCII** constraint itself. Any Unicode character above U+007F (e.g., `pässreset@32473`) will pass validation and silently break downstream RFC 5424 parsers.

The inverse also applies: control characters (U+0000–U+001F, U+007F) pass the current check but are forbidden by PRINTUSASCII. `SiemSyslogFormatter.EscapeSd` strips control chars from *values* but the validator runs against the *SD-ID* (the bracket name), which is emitted at `SiemSyslogFormatter.cs:52` without going through `EscapeSd`.

**Evidence:**
```csharp
// Line 44-52 — no char-class check
if (string.IsNullOrEmpty(syslog.SdId)
    || syslog.SdId.Length > 32
    || syslog.SdId.IndexOfAny([' ', '=', ']', '"']) >= 0)
```

**Fix:** Replace with an explicit PRINTUSASCII allowlist regex:
```csharp
private static readonly Regex SdIdRegex =
    new(@"^[!-~]{1,32}$", RegexOptions.Compiled); // 0x21-0x7E
// ...then exclude the forbidden four:
if (string.IsNullOrEmpty(syslog.SdId)
    || !SdIdRegex.IsMatch(syslog.SdId)
    || syslog.SdId.IndexOfAny([' ', '=', ']', '"']) >= 0)
```

---

### IN-05: `SiemSyslogFormatter.StripControlChars` allocates and then trims — can use a direct builder

**File:** [src/PassReset.Web/Services/SiemSyslogFormatter.cs:79-89](src/PassReset.Web/Services/SiemSyslogFormatter.cs#L79)
**Issue:**
`string.Create` with a fixed-size span followed by `TrimEnd('\0')` is a two-pass approach that (a) allocates two strings when any control char is present and (b) has a subtle bug: if the *legitimate* input contains a null char U+0000 that survives the filter (it's filtered here because `\x20 > 0`), `TrimEnd('\0')` would aggressively strip it. Currently safe only because the filter rejects `\0`.

A small `StringBuilder`-based implementation avoids the extra allocation and is clearer:
```csharp
private static string StripControlChars(string input)
{
    if (!input.Any(c => c < '\x20' || c == '\x7F')) return input; // fast path
    var sb = new StringBuilder(input.Length);
    foreach (var ch in input)
        if (ch >= '\x20' && ch != '\x7F') sb.Append(ch);
    return sb.ToString();
}
```

**Fix:** Apply the above pattern. Out of v1 perf scope technically — flagged for maintainability.

---

## Info (LOW)

### IN-06: `GenericErrorMappingTests.Dispose` only calls `SuppressFinalize` — redundant for a class with no finalizer

**File:** [src/PassReset.Tests/Web/Controllers/GenericErrorMappingTests.cs:33](src/PassReset.Tests/Web/Controllers/GenericErrorMappingTests.cs#L33)
**Issue:**
`public void Dispose() => GC.SuppressFinalize(this);` — the class has no finalizer, no unmanaged resources, and the factories used inside each test are already wrapped in `using var`. Implementing `IDisposable` here is misleading (suggests there is cleanup work) and is only required by the comment saying "Per-test factory disposal keeps rate-limiter partition state isolated" — but that isolation is already achieved by the `using var factory = new ...Factory()` inside each fact.

**Fix:** Remove `IDisposable` from the class declaration and delete the `Dispose` method. Update the comment to note that per-test isolation comes from `using var` in each fact body.

---

### IN-07: `InternalsVisibleTo` grant is broad; consider restricting or documenting the leak surface

**File:** [src/PassReset.Web/PassReset.Web.csproj:17](src/PassReset.Web/PassReset.Web.csproj#L17)
**Issue:**
`<InternalsVisibleTo Include="PassReset.Tests" />` exposes **all** `internal` members of the Web project to tests. The Web project has these internal types (per grep): `PasswordExpiryNotificationService`, `NoOpEmailService`, `DebugPasswordChangeProvider`, `SiemService`, `StartupValidationFailureLogger`, `SmtpEmailService`.

None of these contain hard-coded secrets or security-sensitive defaults, so there is no **leak** in the literal sense. However:
- `SmtpEmailService` and `DebugPasswordChangeProvider` have constructors that accept credentials — a misbehaving test could construct one with a real password and log it. Low risk today (`SmtpEmailService` gets the password from `IOptions<SmtpSettings>`, not a ctor arg), but the attack surface is broader than the STAB-015 tests actually need.
- The PR description says this grant was added for the STAB-015 tests specifically; the STAB-015 tests that landed (`AuditEventRedactionTests`, `SiemSyslogFormatterTests`) only access `public` types. `AuditEvent`, `SiemSyslogFormatter`, and `SiemEventType` are all public. **`InternalsVisibleTo` may not actually be needed for the new tests.**

**Fix:** Verify whether the `InternalsVisibleTo` is used by any current test (grep `PassReset.Web.Services.SiemService` or `DebugPasswordChangeProvider` direct type access in tests — if only used for `typeof(...)` against `public` types, the grant is dead weight). If unused, remove it. If used, document *which* tests rely on it in a comment next to the line.

---

## No Issues Found (Verified Clean)

- **STAB-013 redaction completeness** — `IsAccountEnumerationCode` correctly matches only `InvalidCredentials` and `UserNotFound` per D-01. ModelState validation errors bypass the collapse (they go through `ApiResult.FromModelStateErrors(ModelState)` at `PasswordController.cs:155`, not through `RedactIfProduction`). Other error codes (`PortalLockout`, `ApproachingLockout`, `ChangeNotPermitted`, `PasswordTooYoung`, etc.) correctly pass through unchanged.
- **STAB-015 AuditEvent DTO shape** — `AuditEvent.cs` contains no `Password`, `Token`, `PrivateKey`, `Secret`, or `ApiKey` properties. Reflection test in `AuditEventRedactionTests` locks this at build time via regex `"password|token|secret|privatekey|apikey"` and allowlist comparison to exactly six field names. Compile-time redaction invariant holds.
- **STAB-015 RFC 5424 escape rules** — `SiemSyslogFormatter.EscapeSd` correctly escapes `\`, `"`, `]` in that order (the order matters — escaping `\` first prevents double-escaping the later `\` insertions). `SiemSyslogFormatterTests.Format_WithAuditEvent_EscapesQuotesBracketsBackslashInUsername` covers the injection-resistance path. Control-char stripping is covered by `EscapeSd_StripsControlCharacters`.
- **STAB-015 SdId validator regex** — correctly rejects empty/length>32/forbidden-chars per RFC 5424 §6.3.2 (except the PRINTUSASCII gap called out in IN-04).
- **STAB-016 middleware correctness** — HSTS header construction at `Program.cs:258` is string-literal, no interpolation, no injection surface. `EnableHttpsRedirect` check is a bool — thread-safe. The per-request IOptions resolve is safe, just suboptimal (IN-01).
- **STAB-016 preload protection** — `HttpsRedirectionTests.HstsHeader_NoPreloadDirective` regression-guards D-12.
- **STAB-017 env-var precedence** — `EnvVar_SmtpSettingsPassword_OverridesAppsettings` proves precedence; `EnvVar_RecaptchaPrivateKey_NotLeakedInGetResponse` proves `[JsonIgnore]` survives env-var sourcing (addresses Pitfall 5).
- **Hot-path no-throw invariants** — All SIEM code paths (`LogEvent(eventType,...)`, `LogEvent(AuditEvent)`, `EmitSyslog`, `EmitSyslogStructured`) wrap the delivery in `try { ... } catch (Exception ex) { _logger.LogError(...); }`. `ValidateRecaptchaAsync` catches `HttpRequestException`, `TaskCanceledException`, and `Exception` — no unhandled throw escapes to the hot path.
- **Nullability** — All new source files have `<Nullable>enable</Nullable>` inherited from the csproj. `AuditEvent` uses `string?` for optional fields; `RedactIfProduction` does not accept `null`. No null-deref indicators found.
- **Secret logging** — No `_logger.LogInformation($"...{password}...")`-style log statements introduced. `_logger.LogError` in the reCAPTCHA catch blocks logs the exception and the IP, not the secret.
- **Test isolation (STAB-014)** — `RateLimit_FirstRequestInFreshFactory_Succeeds` explicitly regression-guards rate-limiter partition leakage. Per-test `using var factory = new RateLimitFactory()` pattern is the ASP.NET Core canonical fix for Pitfall 1.

---

## Focus-Area Scorecard

| Focus (from prompt) | Finding(s) | Status |
|---|---|---|
| 1. STAB-013 redaction completeness | Clean. `IsAccountEnumerationCode` is exhaustive per D-01; ModelState preserved. | ✓ |
| 2. STAB-014 rate-limit test isolation | Clean — per-test factory pattern correct. `RequiresInternet` trait applied to Google-dependent test. | ✓ |
| 3. STAB-015 secret leakage / SD escapes / SdId regex | Clean on secret leakage. SdId regex has minor PRINTUSASCII gap (IN-04). Escape rules correct. | ⚠ (IN-04) |
| 4. STAB-016 middleware correctness | Per-request IOptions resolve is safe but suboptimal (IN-01). No thread-safety issue. | ⚠ (IN-01) |
| 5. STAB-017 env-var test hygiene | Cross-class parallelization hazard (WR-03). | ⚠ (WR-03) |
| 6. InternalsVisibleTo leak | No leak; possibly unused grant (IN-07). | ⚠ (IN-07) |
| 7. General nullability / secret logging / SIEM no-throw | Clean. | ✓ |

---

_Reviewed: 2026-04-17_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
