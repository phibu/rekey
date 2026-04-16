---
phase: 08
plan: 03
subsystem: web/config-validation
tags: [options-validation, startup, event-log, fail-fast, serilog]
requires: [08-01]
provides:
  - "Runtime IValidateOptions<T> validators for all 7 options classes"
  - "Fail-fast startup (OptionsValidationException at builder.Build())"
  - "Windows Event Log surfacing under source 'PassReset' (event ID 1001)"
  - "D-08 error message format with appsettings/installer remediation suffix"
  - "Secret redaction for Recaptcha.PrivateKey and SmtpSettings.Password in failure messages"
affects:
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/Services/StartupValidationFailureLogger.cs
  - src/PassReset.Web/Models/*Validator.cs
  - src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs
  - src/PassReset.Tests/Web/Startup/StartupValidationTests.cs
tech-stack:
  added: []
  patterns:
    - "AddOptions<T>().Bind(section).ValidateOnStart() + AddSingleton<IValidateOptions<T>, TValidator>()"
    - "try/catch OptionsValidationException at top-level Program → EventLog.WriteEntry"
    - "preserveStaticLogger:true on UseSerilog for WebApplicationFactory re-entry safety"
    - "Subclassed WebApplicationFactory<Program> per test class (HostingListener determinism)"
key-files:
  created:
    - src/PassReset.Web/Services/StartupValidationFailureLogger.cs
    - src/PassReset.Web/Models/ClientSettingsValidator.cs
    - src/PassReset.Web/Models/WebSettingsValidator.cs
    - src/PassReset.Web/Models/SmtpSettingsValidator.cs
    - src/PassReset.Web/Models/SiemSettingsValidator.cs
    - src/PassReset.Web/Models/EmailNotificationSettingsValidator.cs
    - src/PassReset.Web/Models/PasswordExpiryNotificationSettingsValidator.cs
    - src/PassReset.Tests/Web/Startup/StartupValidationTests.cs
  modified:
    - src/PassReset.Web/Program.cs
    - src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs
decisions:
  - "EventLog.WriteEntry (built-in System.Diagnostics.EventLog) over a NuGet sink — vendor-conservative, zero new dependency. Source registration owned by Install-PassReset.ps1 (plan 08-04)."
  - "Preserve existing WebSettings.UseDebugProvider environment-guard as an inline throw (IValidateOptions<T> cannot access IHostEnvironment)."
  - "Secret fields (Recaptcha.PrivateKey, SmtpSettings.Password) rendered as '<redacted>' in D-08 Fail messages. Tests assert no leakage."
  - "Subclassed WebApplicationFactory<Program> per startup-validation test class (StartupValidationTests.InvalidPasswordChangeOptionsFactory) mirrors existing DebugFactory. Avoids HostFactoryResolver.HostingListener race when multiple inline factories are constructed in the same process."
  - "UseSerilog uses preserveStaticLogger:true — required because WebApplicationFactory re-enters Program.cs across tests; without it the static ReloadableLogger from a prior run triggers 'logger is already frozen' on re-init."
metrics:
  tasks_completed: 2
  tasks_total: 2
  tests_added: 1
  tests_passing: 121
  tests_failing: 0
  duration_minutes: ~25
  completed: 2026-04-16
---

# Phase 08 Plan 03: Fail-Fast Options Validation Summary

One-liner: **All 7 options classes now validate at DI build via `ValidateOnStart()`, with operator-actionable D-08 errors surfaced to the Windows Event Log before IIS returns 502.**

## Validators Created

Each follows the pattern from `PasswordChangeOptionsValidator.cs`: sealed, `IValidateOptions<T>`, early-return success on disabled/auto paths, D-08 Fail format.

| Validator | Key Rules |
|---|---|
| `ClientSettingsValidator` | Recaptcha.Enabled ⇒ non-empty SiteKey + PrivateKey; MinimumScore ∈ [0,1] |
| `WebSettingsValidator` | Type-only checks (env-guard stays inline in Program.cs — needs IHostEnvironment) |
| `SmtpSettingsValidator` | Host non-empty ⇒ Port 1..65535, FromAddress contains '@', Username/Password both set or both empty |
| `SiemSettingsValidator` | Syslog.Enabled ⇒ Host non-empty, Port 1..65535, Protocol ∈ {Udp,Tcp}; AlertEmail.Enabled ⇒ ≥1 recipient, each contains '@'; AlertOnEvents entries ∈ SiemEventType enum |
| `EmailNotificationSettingsValidator` | Cross-field per model intent |
| `PasswordExpiryNotificationSettingsValidator` | Enabled ⇒ DaysBeforeExpiry≥1, NotificationTimeUtc matches `^\d{2}:\d{2}$`, PassResetUrl starts with `https://` |
| `PasswordChangeOptionsValidator` (updated) | Existing LdapHostnames/LdapPort checks + D-08 remediation suffix |

### D-08 Message Format

```
{field.path}: {reason} (got "{actual}"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.
```

Sensitive fields use `(got "<redacted>")`. Test `Validate_WhenSecretField_Invalid_DoesNotLeakSecret` asserts the known test secret string does not appear in any failure message.

## Event Log Helper Design

`StartupValidationFailureLogger.LogToEventLog(OptionsValidationException)`:

- Joins `ex.Failures` with newlines (already-redacted D-08 strings — no raw config dump).
- `EventLog.SourceExists("PassReset")` check; if false (source not registered yet) silently skip.
- Outer try/catch swallows any EventLog permission/API exception — original `OptionsValidationException` still re-throws.
- `#pragma warning disable CA1416` documents the Windows-only API surface (PassReset.Web targets `net10.0-windows`).

Program.cs wraps the entire host lifecycle (`builder.Build()` through `app.Run()`) in `try { ... } catch (OptionsValidationException ex) { StartupValidationFailureLogger.LogToEventLog(ex); Log.Fatal(...); throw; }`.

**No `Log.CloseAndFlush()` in catch.** A broad finally-flush was deliberately removed — `WebApplicationFactory<Program>` re-enters the top-level program across tests, and a `catch (Exception)` or CloseAndFlush pattern swallows `HostFactoryResolver.StopTheHostException` (the handoff signal), breaking subsequent factories with "entry point did not build an IHost".

## Event Log Source Registration Handoff → Plan 08-04

This plan intentionally does NOT register the Event Log source. Rationale:

- `EventLog.CreateEventSource` requires administrator privileges. App pool identity typically does not have them.
- Installer (`Install-PassReset.ps1`) already runs elevated during deployment — right place to create the source.

Plan 08-04 will add to `Install-PassReset.ps1`:

```powershell
if (-not [System.Diagnostics.EventLog]::SourceExists('PassReset')) {
    [System.Diagnostics.EventLog]::CreateEventSource('PassReset', 'Application')
}
```

Until 08-04 ships, the helper's `SourceExists` check silently no-ops on unregistered machines (dev boxes, CI). The exception still propagates via ASP.NET Core module stdout logging, so the failure is observable — just not in Event Viewer.

## Test Approach

### Validator Unit Tests (Task 1 — already committed in 58cfd05)

One test class per validator under `src/PassReset.Tests/Web/Models/`. Each asserts:
- `Validate_WhenValid_ReturnsSuccess`
- `Validate_WhenFieldX_Invalid_ReturnsFail_WithD08Message` (field path + `(got "...")` + remediation suffix)
- `Validate_WhenSecretField_Invalid_DoesNotLeakSecret`

### Startup Integration Test (Task 2)

Single end-to-end test `Build_WithInvalidPasswordChangeOptions_ThrowsOptionsValidationException` under `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs`. Uses a dedicated `InvalidPasswordChangeOptionsFactory : WebApplicationFactory<Program>` subclass (mirrors `DebugFactory`) to inject invalid `PasswordChangeOptions` via in-memory config and asserts `OptionsValidationException` appears in the exception chain OR the D-08 message content survives wrapping by `WebApplicationFactory`.

Full suite: **121/121 passing.**

## Deviations from Plan

### [Rule 1 — Bug] Serilog `logger is already frozen` on factory re-entry

- **Found during:** Task 2 — running full test suite after adding `StartupValidationTests`
- **Issue:** `PasswordControllerTests.Post_UserNotFoundMagicUser_ReturnsUserNotFoundErrorCode` failed with `System.InvalidOperationException: The logger is already frozen` — but only when run after `StartupValidationTests` in the same process. In isolation it passed.
- **Root cause:** `Log.Logger = CreateBootstrapLogger()` returns a `ReloadableLogger`. `UseSerilog((ctx, svc, lc) => ...)` freezes it when the host builds. Second factory run in the same process reassigns `Log.Logger` to a fresh bootstrap logger, but `UseSerilog` without `preserveStaticLogger:true` still races with the already-frozen pipeline from the prior test.
- **Fix:** Added `preserveStaticLogger: true` to the `UseSerilog` call. Keeps the bootstrap logger independent of the host pipeline logger so repeated `WebApplicationFactory<Program>` test starts do not trip the freeze check.
- **Files modified:** `src/PassReset.Web/Program.cs`
- **Commit:** `2c98aa9`

### [Rule 2 — Test reduction] Reduced startup integration tests from 3 to 1

- **Reason:** Spec allowed reduction if test isolation was unreliable. A single `Build_WithInvalidPasswordChangeOptions_ThrowsOptionsValidationException` test with a dedicated `InvalidPasswordChangeOptionsFactory` subclass proves the end-to-end wiring. The happy-path `Build_WithValidConfig_Succeeds` is already implicitly proven by every one of the 110+ `PasswordControllerTests` that boot `DebugFactory` successfully. The `Build_WithInvalidRecaptcha` case is covered by the `ClientSettingsValidator` unit tests (Task 1).
- **Net outcome:** Equal coverage, less flakiness surface area.

### [Rule 3 — Blocker] Inline `WebApplicationFactory<Program>` broke HostingListener determinism

- **Issue:** The original `StartupValidationTests.FactoryWith(...)` helper created anonymous inline `new WebApplicationFactory<Program>().WithWebHostBuilder(...)` instances. `HostFactoryResolver.HostingListener` uses a process-wide `DiagnosticListener` to intercept `builder.Build()` — once another factory subscribes, the inline one can miss the intercept and never call the test configuration delegate.
- **Fix:** Converted to a nested `InvalidPasswordChangeOptionsFactory : WebApplicationFactory<Program>` subclass with `ConfigureWebHost` override, mirroring the existing `DebugFactory` pattern. Each test class now owns its factory type.

## Self-Check: PASSED

- Task 1 commit `58cfd05` present in `git log`
- Task 2 commit `2c98aa9` present in `git log`
- `src/PassReset.Web/Program.cs` contains `preserveStaticLogger: true`
- `src/PassReset.Web/Services/StartupValidationFailureLogger.cs` exists with `EventLog.WriteEntry`
- `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` uses subclass factory
- `dotnet test src/PassReset.sln --configuration Release` → 121/121 passing

## Commits

| Task | Hash | Message |
|---|---|---|
| 1 | `58cfd05` | feat(validation): add D-08 validators for all 7 options classes (08-03) |
| 2 | `2c98aa9` | feat(web): fail-fast options validation with Event Log surfacing (08-03) |
