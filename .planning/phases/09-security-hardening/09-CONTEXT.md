# Phase 9: Security Hardening - Context

**Gathered:** 2026-04-17
**Status:** Ready for planning

<domain>
## Phase Boundary

Production deployments of PassReset resist account enumeration, enforce rate-limit + reCAPTCHA with test coverage, ship a structured audit trail with strict secret redaction, guarantee HTTPS/HSTS behavior, and allow SMTP/LDAP/reCAPTCHA secrets to be sourced from environment variables instead of plaintext `appsettings.Production.json`. Scope covers STAB-013 through STAB-017. Full DPAPI/encryption (V2-003) stays in v2.0 Phase 6.

</domain>

<decisions>
## Implementation Decisions

### STAB-013 — Generic error mapping (account enumeration)
- **D-01:** Collapse **only** `InvalidCredentials` and `UserNotFound` to a single production response. All other codes (`PortalLockout`, `ApproachingLockout`, `RateLimitExceeded`, `ChangeNotPermitted`, `PasswordTooYoung`, `PasswordTooRecentlyChanged`, validation errors) retain their specific `ApiErrorCode` values — they don't leak account existence and UX needs them.
- **D-02:** Collapse uses existing `ApiErrorCode.Generic` (0). Do **not** introduce a new `AuthenticationFailed` enum member — keeps `ApiErrorCodeTests.EnumMemberCount_LocksInKnownSurface` at 20 and avoids the TypeScript mirror update.
- **D-03:** Toggle is environment-based via `IHostEnvironment.IsProduction()`. No `WebSettings` config flag. Dev/Test continue to see the real code for debuggability.
- **D-04:** Client copy reuses the existing "The credentials you supplied are not valid." message (current UI string for `InvalidCredentials`). No new i18n key.
- **D-05:** SIEM stays granular: `MapErrorCodeToSiemEvent` in `PasswordController` is **not** modified — it keeps emitting `InvalidCredentials` vs `UserNotFound` events so operators can detect enumeration attacks. Only the wire response collapses.

### STAB-014 — Rate-limit + reCAPTCHA test coverage
- **D-06:** Tests go in a new test class that uses `WebApplicationFactory<Program>` (Program.cs already has `public partial class Program { }` and the "WebApplicationFactory re-entry" guards from Phase 8).
- **D-07:** Cover four scenarios: (a) rate-limit enforced — 6th request in window returns 429 + SIEM `RateLimitExceeded` event; (b) rate-limit disabled/bypassed path if any config branch exists; (c) reCAPTCHA enabled path — invalid token returns `InvalidCaptcha` + SIEM `RecaptchaFailed`; (d) reCAPTCHA disabled path — missing token accepted when `Recaptcha.Enabled = false` or `PrivateKey` empty.
- **D-08:** Shared fixture pattern: use a test-only `IPasswordChangeProvider` stub (reuse `DebugPasswordChangeProvider` where possible) so tests never hit AD.

### STAB-015 — Structured audit events + redaction
- **D-09:** Extend `SiemService` with structured fields rather than adding a parallel audit sink. Single source of truth. Keep RFC 5424 syslog as the transport; add structured data elements for `traceId`, `outcome`, `eventType`, `username`, `clientIp`.
- **D-10:** Redaction uses an **allowlist DTO** — introduce `AuditEvent` record with only permitted fields (`EventType`, `Outcome`, `Username`, `ClientIp`, `TraceId`, `Detail`). No `Password`, `CurrentPassword`, `NewPassword`, `PrivateKey`, or `Token` field exists on the DTO, so they cannot be logged by construction. Compile-time safety beats runtime scrubbing.
- **D-11:** Audit the current 10 `SiemEventType` enum members against STAB-015's "attempts, failures, rate-limit blocks, successes" list during research. **Do not** pre-emptively add an `AttemptStarted` event — only add one if research proves a coverage gap. Prefer reusing existing events (`PasswordChanged`, `InvalidCredentials`, `UserNotFound`, `RateLimitExceeded`, `ValidationFailed`, `RecaptchaFailed`, `ChangeNotPermitted`, `PortalLockout`, `ApproachingLockout`, `Generic`).

### STAB-016 — HTTPS/HSTS enforcement
- **D-12:** Keep the app-level `UseHttpsRedirection()` + HSTS header (currently gated by `WebSettings.EnableHttpsRedirect`). No change to header values: `max-age=31536000; includeSubDomains` stays. **Do not** add HSTS preload by default — that's org-policy territory and irreversible for operators.
- **D-13:** Add an installer binding check in `Install-PassReset.ps1`: if the site has an HTTP binding but no HTTPS binding, **warn** using the Phase 7 `Write-Warn` helper. If both HTTP and HTTPS exist, accept (HTTP is valid for redirect). If HTTPS is missing entirely, warn loudly but do not block — operators running offline staging need the escape hatch. Do **not** auto-delete bindings.
- **D-14:** Keep `WebSettings.EnableHttpsRedirect` as the config knob (don't force-enable in Production). This preserves the escape hatch for TLS-terminating reverse proxy setups where the app sees plain HTTP from the proxy but the client-facing scheme is HTTPS.

### STAB-017 — Env-var secrets
- **D-15:** In-scope secrets: `SmtpSettings.Password`, `PasswordChangeOptions.ServiceAccountPassword` (if present for non-`UseAutomaticContext` deployments), `ClientSettings.Recaptcha.PrivateKey`. Non-secret values (SMTP host, LDAP domain) stay in appsettings — keeping to STAB-017's "stepping stone" scope.
- **D-16:** Naming: default ASP.NET Core double-underscore convention (`SmtpSettings__Password`, `PasswordChangeOptions__ServiceAccountPassword`, `ClientSettings__Recaptcha__PrivateKey`). Zero custom code — `ConfigurationBuilder.AddEnvironmentVariables()` handles it. No custom `PASSRESET_` prefix.
- **D-17:** Dev workflow: `dotnet user-secrets` is the documented path. `AddUserSecrets()` is already active in Development via ASP.NET Core defaults. Document the commands in `CONTRIBUTING.md` and/or `docs/Secret-Management.md`. `appsettings.Development.json` remains allowed (STAB-017 text permits it) but user-secrets is the recommended dev flow.
- **D-18:** Installer does **not** set env vars. Document AppPool env-var setup in `docs/IIS-Setup.md` and `docs/Secret-Management.md` (appcmd syntax). Keeping installer plaintext-free aligns with the v2.0 DPAPI trajectory.

### Claude's Discretion
- Exact structure of the audit JSON structured-data element (RFC 5424 SD-ID + param names) — research informs.
- Whether the existing `ClientSettings.Recaptcha.PrivateKey [JsonIgnore]` needs additional validator rules when env-var sourced.
- Test class naming and file organization within `PassReset.Tests`.
- Specific wording of the installer `Write-Warn` message for binding check.
- Whether Program.cs gains a helper method for the IsProduction error collapse, or it's inlined at the controller's error-return path.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase scope and requirements
- `.planning/REQUIREMENTS.md` §"Security Hardening (Phase 9)" — STAB-013..017 acceptance criteria
- `.planning/ROADMAP.md` §"Phase 9: Security Hardening" — success criteria + cross-phase dependencies
- `.planning/PROJECT.md` §"Active (v1.4.0 — Stabilization)" — phase scope + key decisions

### Prior phase context (carry-forward decisions — DO NOT redesign)
- `.planning/phases/07-installer-deployment-fixes/07-CONTEXT.md` — Phase 7 established `Write-Step`/`Write-Ok`/`Write-Warn`/`Abort` helpers, `-Force` = safe-default unattended mode, `[CmdletBinding(SupportsShouldProcess)]` on installer. STAB-016 installer check MUST reuse these.
- `.planning/phases/08-config-schema-sync/08-CONTEXT.md` — Phase 8 established Options pattern with `AddOptions<T>().Bind().ValidateOnStart()`, `IValidateOptions<T>` validators, `StartupValidationFailureLogger` → Windows Event Log. STAB-017 env-var validation hooks into this.

### Code surfaces being modified (read before editing)
- `src/PassReset.Web/Controllers/PasswordController.cs` — POST handler, `MapErrorCodeToSiemEvent`, rate-limit/reCAPTCHA hand-off (STAB-013, STAB-014)
- `src/PassReset.Common/ApiErrorCode.cs` — pinned enum at 20 members; `PassReset.Tests/Common/ApiErrorCodeTests.cs` enforces count
- `src/PassReset.Web/Program.cs` — security headers block, rate-limiter registration, options binding (STAB-014, STAB-016, STAB-017)
- `src/PassReset.Web/Services/SiemService.cs` + `SiemSyslogFormatter` — structured-data extension point (STAB-015)
- `src/PassReset.Web/Models/SiemSettings.cs`, `SiemEventType` enum (STAB-015)
- `src/PassReset.Web/Models/WebSettings.cs` — `EnableHttpsRedirect` (STAB-016)
- `src/PassReset.Web/Models/SmtpSettings.cs`, `ClientSettings.Recaptcha` — env-var binding (STAB-017)
- `deploy/Install-PassReset.ps1` — binding check (STAB-016)
- `src/PassReset.Web/ClientApp/src/types/settings.ts` — `ApiErrorCode` mirror (verify no change needed for STAB-013 since we reuse `Generic`)

### Operator-facing docs to update
- `docs/IIS-Setup.md` — env-var AppPool setup (STAB-017), HTTPS binding requirement (STAB-016)
- `docs/Secret-Management.md` — env-var + user-secrets workflow (STAB-017), update hardening options section
- `docs/appsettings-Production.md` — document env-var overrides for `SmtpSettings.Password`, `PasswordChangeOptions.ServiceAccountPassword`, `ClientSettings.Recaptcha.PrivateKey`
- `docs/Known-Limitations.md` — note the collapsed error response in production (STAB-013) so operators understand SIEM-vs-wire-response divergence
- `CONTRIBUTING.md` — `dotnet user-secrets` commands for local dev (STAB-017)
- `CHANGELOG.md` — entries for STAB-013..017 under v1.4.0 `[Unreleased]`

### External references (research)
- ASP.NET Core environment variables configuration provider — double-underscore path semantics
- `dotnet user-secrets` CLI — standard commands, Development-only activation
- RFC 5424 structured-data element syntax — SD-ID naming rules for STAB-015 audit fields
- IIS `appcmd set config /section:applicationPools` — AppPool environment variable documentation

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `SiemService.LogEvent(SiemEventType, username, ip, detail?)` — already called from `PasswordController.Audit`. STAB-015 extends payload, not plumbing.
- `SiemSyslogFormatter.Format(...)` — pure static helper (testable without sockets). Add structured-data parameter here.
- `SiemEventType` enum — 10 members already cover most STAB-015 categories.
- `MapErrorCodeToSiemEvent` — already preserves granularity; STAB-013 only changes what is returned, not what is logged.
- `DebugPasswordChangeProvider` — test stub for STAB-014 integration tests; avoids AD dependency.
- `Program.cs` is `public partial class Program { }` — `WebApplicationFactory<Program>` already works (prior phases solved the test re-entry issues).
- Phase 7 installer helpers (`Write-Step`/`Write-Ok`/`Write-Warn`/`Abort`) — reuse for STAB-016 binding check.
- Phase 8 options validator pattern (`IValidateOptions<T>` + `ValidateOnStart()`) — reuse for any env-var-sourced secret rules.
- `StartupValidationFailureLogger.LogToEventLog(ex)` — missing-secret diagnostics pathway if STAB-017 introduces required-field rules.

### Established Patterns
- **Error codes over exceptions:** `PerformPasswordChangeAsync` returns `ApiErrorItem?`; controllers serialize into `ApiResult`. STAB-013 collapse happens at the controller serialization edge, not inside providers.
- **SIEM must never throw on the hot path:** `SiemService.LogEvent` swallows all failures. STAB-015 structured fields must preserve this invariant.
- **Request-scoped logging context:** `using var requestScope = _logger.BeginScope(...)` with `Username` / `TraceId` / `ClientIp`. Reuse for structured audit.
- **Rate-limit partitions by IP:** per-IP fixed-window already wired (`password-fixed-window`, `pwned-check-window`). STAB-014 tests must reset partition state between test cases — use a unique IP per test or factory-level reset.
- **Environment-gated behavior:** `builder.Environment.IsDevelopment()` and similar checks pattern exists (e.g., `UseDebugProvider` production guard). STAB-013 `IsProduction()` follows this precedent.
- **Installer prompts:** Phase 7 `Write-Warn` + `-Force` flow. Warnings do not block `-Force`; prompts only fire in interactive mode.

### Integration Points
- `PasswordController.PostAsync` error-return paths → STAB-013 collapses here before `BadRequest(result)`.
- `PasswordController.Audit` → `ISiemService.LogEvent` → STAB-015 structured fields flow via new `AuditEvent` DTO.
- `Program.cs` security headers middleware block → STAB-016 no change (HSTS already present).
- `Program.cs` `AddOptions<SmtpSettings>().Bind(...)` / `ClientSettings.Recaptcha` → STAB-017 env-var overrides work automatically through `AddEnvironmentVariables()`.
- `Install-PassReset.ps1` post-install verification section → STAB-016 binding check slots in here, after HTTPS cert bind step.
- `tests/PassReset.Tests/*` — STAB-014 integration tests land in a new class; reuse fixtures where possible.

</code_context>

<specifics>
## Specific Ideas

- **Minimal enum churn is a priority:** avoid touching `ApiErrorCode` or `SiemEventType` unless research proves a gap. The pinned 20-member `ApiErrorCode` test and TypeScript mirror make every addition costly.
- **Stepping-stone framing for STAB-017:** this phase gets env-var sourcing working; DPAPI/encrypted-at-rest is explicitly deferred to v2.0 Phase 6 (V2-003). Don't over-engineer a secrets abstraction here.
- **SIEM granularity is operator-visible:** Security operators depend on distinguishing `InvalidCredentials` from `UserNotFound` to detect enumeration attacks. The wire-response collapse for STAB-013 must not bleed into SIEM emission.
- **Belt-and-suspenders for HTTPS:** app-level + installer check together. App-level works for dev/CI; installer check catches the "operator added HTTP binding and forgot HTTPS" failure mode that STAB-016 targets.
- **Allowlist DTO for redaction:** prefer compile-time safety (no password field exists on the DTO) over runtime scrubbers that can miss new fields.

</specifics>

<deferred>
## Deferred Ideas

- **DPAPI / encrypted secrets at rest** — v2.0 Phase 6 (V2-003). STAB-017 is the stepping stone.
- **HSTS preload** — operator decision; not forced by this phase. Documentable in hardening guide later.
- **AuthenticationFailed dedicated error code** — rejected in STAB-013 discussion; would bump enum count and TypeScript mirror. Generic (0) is sufficient for the wire.
- **Custom PASSRESET_ env-var prefix** — rejected; ASP.NET Core double-underscore is sufficient and zero-code.
- **Installer auto-setting of AppPool env vars** — rejected; operators own secret injection for this phase.
- **Dedicated Serilog audit sink in parallel to SIEM** — rejected; single source of truth in SIEM.
- **Removing `WebSettings.EnableHttpsRedirect` knob** — rejected; TLS-terminating proxy deployments need the escape hatch.

</deferred>

---

*Phase: 09-security-hardening*
*Context gathered: 2026-04-17*
