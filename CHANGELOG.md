# Changelog

All notable changes to PassReset are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/).

---

## [Unreleased]

(nothing yet)

---

## [2.0.0-alpha.7] — 2026-04-22

Completes the PowerShell 7 compatibility migration that began in alpha.4. All remaining `WebAdministration` call sites that depend on typed-object graphs crossing the WinPSCompat proxy have been ported to `IISAdministration` equivalents. No application code changes.

### Fixed

- **HTTPS binding cert attachment** — `$binding.AddSslCertificate($thumbprint, 'My')` was a WinPSCompat-proxied instance method; methods are stripped across the deserialization boundary, so this call was guaranteed to fail on every PS 7 IIS install with `-CertThumbprint`. Replaced with `New-IISSiteBinding -CertificateThumbPrint <hash> -CertStoreLocation 'My'`, which takes the cert as a direct parameter on an IISAdministration-native cmdlet. *(installer)*
- **Port-80 conflict detection** — `Get-WebBinding | Where { $_.ItemXPath -match "name='...'" }` relied on an ETS (Extended Type System) property that doesn't survive WinPSCompat serialization. Replaced with a direct walk of `system.applicationHost/sites` via `Get-IISConfigCollection`. *(installer)*
- **`Test-PortFree` IIS-binding ownership check** — `$iisBinding.Bindings.Collection.bindingInformation` is a nested ETS chain that becomes `$null` across the proxy boundary. Replaced with `Get-IISSiteBinding`, which returns objects whose `.BindingInformation` is a real .NET string property. *(installer)*
- **HTTP/HTTPS binding removal** — `$existingBinding | Remove-WebBinding` pipeline failed to bind parameters on deserialized proxy objects. Replaced with explicit `Remove-IISSiteBinding -Name -BindingInformation -Protocol -Confirm:$false`. *(installer)*
- **App pool environment variables** — `Get-WebConfigurationProperty -PSPath 'MACHINE/WEBROOT/APPHOST'` is part of the same `IIS:\` drive family that doesn't work from the PS 7 caller runspace. Replaced with the `Get-IISConfigCollection` / `New-IISConfigCollectionElement` pattern (wrapped in `Start-IISCommitDelay` / `Stop-IISCommitDelay -Commit $true`). *(installer)*
- **`Restore-StoppedForeignSites`** — defensive `$script:IISAvailable` guard added so the handler cannot emit a confusing "Start-Website not recognized" error if it runs before the modules load. *(installer, hygiene)*

### Verdict

After six installer-focused alphas, the installer now uses `IISAdministration` for every typed-object / drive-dependent path and `WebAdministration` only for plain verb-noun cmdlets (`New-Website`, `Start-Website`, `Stop-Website`, `New-WebAppPool`, etc.) that have no proxy-vulnerable members. No more `WebAdministration`-proxy-shaped bugs should surface.

---

## [2.0.0-alpha.6] — 2026-04-22

Fifth installer hardening pass. No application code changes.

### Fixed

- **`Get-IISConfigCollection` pipeline enumeration on PS 7.** Alpha.5 used a three-stage pipeline (`Get-IISConfigSection | Get-IISConfigCollection | Get-IISConfigCollectionElement`) which [Microsoft docs warn against](https://learn.microsoft.com/en-us/powershell/module/iisadministration/get-iisconfigcollection) but is often tolerated on PS 5.1. On PS 7 via WinPSCompat, the `ConfigurationCollection` loses its pipeline-enumeration-suppression attribute across the session proxy boundary, so PowerShell iterates each element and passes individual `ConfigurationElement` objects to `Get-IISConfigCollectionElement` — which expects a `ConfigurationCollection` and fails binding with `"The input object cannot be bound to any parameters for the command"`. Switched to the docs-recommended two-step pattern: assign the collection to a local, then pass explicitly via `-ConfigCollection`. Fixed at three call sites (identity read, app pool write, site upgrade write). *(installer)*

---

## [2.0.0-alpha.5] — 2026-04-22

Fourth installer hardening pass — fixes three real errors reported on a PS 7.6 fresh IIS install of alpha.4. No application code changes.

### Fixed

- **`New-EventLog` not recognized on PS 7.** The legacy `New-EventLog` cmdlet was removed from PowerShell 7. Switched to the direct .NET call `[System.Diagnostics.EventLog]::CreateEventSource('PassReset', 'Application')`. *(installer)*
- **`$pool.ProcessModel.IdentityType` access fails on PS 7.** IISAdministration's `Get-IISAppPool` returns a `Microsoft.Web.Administration.ApplicationPool` through the WinPSCompat remoting session; the deserialized proxy loses the typed `.ProcessModel` object graph, so nested property reads throw "property not found." Migrated all read/write paths to the low-level config API (`Get-IISConfigSection` / `Get-IISConfigCollectionElement` / `Get-IISConfigAttributeValue` / `Set-IISConfigAttributeValue`), which uses string primitives and is immune to the proxy issue. *(installer)*
- **`Stop-IISCommitDelay -Commit` missing argument.** Per [Microsoft docs](https://learn.microsoft.com/en-us/powershell/module/iisadministration/stop-iiscommitdelay), `-Commit` takes a `[Boolean]` value, not a `[switch]`. Changed every call site to `Stop-IISCommitDelay -Commit $true`. *(installer)*
- **Same deserialization fix applied preemptively to site property writes.** The upgrade path that set `$site.Applications['/'].VirtualDirectories['/'].PhysicalPath` had the same latent bug (silently dropped assignments on a deserialized proxy). Migrated to the config API. *(installer)*

---

## [2.0.0-alpha.4] — 2026-04-22

Third installer hardening pass — the PowerShell 7 story is finally correct. No application code changes.

### Fixed

- **Installer now works on PowerShell 7 IIS hosts.** The previous alphas assumed `Import-Module WebAdministration` would make the `IIS:\` PSDrive available for `Set-ItemProperty "IIS:\AppPools\..."` and similar calls. It does not — in PS 7, the `WebAdministration` PSProvider and its `IIS:\` drive live inside the WinPSCompat remoting session and are not reachable from the caller's runspace (confirmed on PS 7.6 with IIS Management Scripting Tools installed). Migrated all drive-dependent call sites to the `IISAdministration` module (PS Core-native, no compat session). Cmdlet-based calls like `Get-Website`, `New-WebAppPool`, `New-WebBinding` still use WebAdministration through compat-session proxies and are unchanged. *(installer)*

### Technical notes

- `Initialize-WebAdministration` → `Initialize-IIS`. Loads `IISAdministration` as primary (config writes), `WebAdministration` as secondary (cmdlet proxies). Drops the dead `New-PSDrive -PSProvider WebAdministration` fallback.
- App pool and site property writes now use `Start-IISCommitDelay` / `Stop-IISCommitDelay -Commit` around property-assignment blocks on `Get-IISAppPool` / `Get-IISSite` results.
- `Test-Path "IIS:\AppPools\..."` / `Test-Path "IIS:\Sites\..."` replaced by `Get-IISAppPool` / `Get-IISSite` existence checks.
- `Get-WebConfigurationProperty -PSPath 'IIS:\'` identity reads replaced by `$pool.ProcessModel.IdentityType` / `.UserName`.

---

## [2.0.0-alpha.3] — 2026-04-22

Second installer hardening pass after a systematic audit of `Install-PassReset.ps1` surfaced five more Severity-1 issues. No application code changes.

### Fixed

- **IIS prerequisite checks (`W3SVC`, `Get-WindowsFeature`) now gated on `-HostingMode IIS`.** Previously ran unconditionally — Service- and Console-mode installs on non-IIS hosts aborted immediately, and `Get-WindowsFeature` itself throws on workstation-class hosts without Server-Manager RSAT. *(installer)*
- **Top-level `Test-Path "IIS:\..."` calls short-circuited on `$script:WebAdministrationAvailable`.** Strict mode previously threw `Cannot find drive 'IIS'` on non-IIS hosts before the hosting-mode branch was reached. *(installer)*
- **`sc.exe delete` exit code now checked in `Install-AsWindowsService`.** Non-zero exits (notably 1072 "marked for deletion" when a stale handle is open) now abort with a guided message telling the operator to close Services.msc / Event Viewer or reboot, instead of silently failing the subsequent `New-Service`. *(installer)*
- **`<install-dir>/keys` ACL is no longer blank-slate overwritten on every upgrade.** Previously `Set-Acl` was called unconditionally with a fresh `DirectorySecurity` that stripped all existing ACEs. If the app-pool identity had changed between installer runs, the Data Protection key ring became unreadable and all stored admin-UI secrets were lost. Now the ACL is applied only on fresh directory creation; upgrades detect identity drift and emit an `icacls` remediation hint rather than rewriting. *(installer, security)*
- **`$keysPath` initialized before mode branches** — the Done-summary line previously referenced an uninitialized variable in Service/Console modes, tripping `Set-StrictMode`. *(installer)*

---

## [2.0.0-alpha.2] — 2026-04-22

Installer hotfix for v2.0.0-alpha.1.

### Fixed
- **`Install-PassReset.ps1` aborted with `Get-Website is not recognized as a name of a cmdlet`** on PS 7 hosts where the `WebAdministration` module wasn't pre-loaded. Phase 14's new hosting-mode resolution called `Get-Website` *before* the existing `Import-Module WebAdministration` guard block. Extracted the module-load + `IIS:\` PSDrive registration into a new `Initialize-WebAdministration` function and call it immediately after the `PASSRESET_TEST_MODE` short-circuit — before any `Get-Website` call. IIS-mode still aborts fatally if the module can't load (same error text as before); Service/Console modes now tolerate the module being missing and fall through to non-IIS code paths. *(installer)*

---

## [2.0.0-alpha.1] — 2026-04-22

First v2.0 alpha. Bundles four phases of work on top of v1.4.2: cross-platform LDAP provider (phase 11), local offline password policy (phase 12), loopback admin UI with encrypted secret storage (phase 13), and pluggable Windows hosting modes — IIS, Windows Service, or Console (phase 14). **Existing IIS deployments upgrade with no config changes**; all new features are opt-in.

### Added

**Phase 11 — Cross-platform LDAP provider**
- **`PassReset.PasswordProvider.Ldap`** — new project (`net10.0`) implementing `IPasswordChangeProvider` via `System.DirectoryServices.Protocols.LdapConnection`. Works on Windows, Linux, and macOS. Passes a shared behavioral contract suite against the Windows provider. *(provider)*
- **`PasswordChangeOptions.ProviderMode`** — new `Auto | Windows | Ldap` enum selecting the active provider. Default `Auto` picks the Windows provider on Windows. Schema + templates updated. *(web, provider)*
- **Service-account LDAP binding** — `ServiceAccountDn`, `ServiceAccountPassword`, `BaseDn`, `LdapHostnames`, `LdapPort`, `LdapUseSsl`, `LdapTrustedCertificateThumbprints`. `ServiceAccountPassword` binds via the `PasswordChangeOptions__ServiceAccountPassword` env var. *(web, provider)*
- **Samba DC CI integration test** — GitHub Actions `integration-tests-ldap` job spins up a Samba AD DC container and runs end-to-end change-password flows against the Ldap provider. *(ci)*
- **`IAdConnectivityProbe`** — narrow AD-reachability seam for the health endpoint. `DomainJoinedProbe` (Windows) + `LdapTcpProbe` (cross-platform) implementations replace the inline `PrincipalContext` / `TcpClient` logic in `HealthController`. *(web, provider, provider-ldap)*
- **`IPrincipalContextFactory`** — Windows-only seam over `PrincipalContext` + `UserPrincipal.FindByIdentity`. Decouples `PasswordChangeProvider` from BCL types directly. *(provider)*

**Phase 12 — Local offline password policy**
- **`LocalPolicyPasswordChangeProvider`** — decorator sitting outermost in the password-change chain, enforcing two optional checks before any AD round-trip:
  - **Banned-words list** — plaintext file, case-insensitive substring match.
  - **Local HIBP SHA-1 corpus** — air-gapped alternative to the remote HIBP API. When `LocalPwnedPasswordsPath` is configured, remote HIBP API calls are disabled automatically.
  See `docs/LocalPasswordPolicy-Setup.md` for operator setup. *(common, web)*
- **New `ApiErrorCode` values** — `BannedWord` (20), `LocallyKnownPwned` (21). Frontend messages are intentionally identical ("This password is not allowed by local policy.") to avoid leaking which list matched. *(common, web)*

**Phase 13 — Admin UI + encrypted secret storage**
- **Loopback admin website at `/admin`** for editing operator-owned configuration. Bound to `127.0.0.1:<LoopbackPort>` via a dedicated Kestrel listener — socket-level enforcement, not reachable over the public HTTPS binding. *(web)*
- **Encrypted secret storage (`secrets.dat`)** via ASP.NET Core Data Protection. Non-secrets remain in plaintext `appsettings.Production.json`; env-var overrides continue to take precedence. *(web)*
- **Operator doc** — see `docs/Admin-UI.md`.

**Phase 14 — Windows hosting modes (IIS / Service / Console)**
- **`-HostingMode` installer parameter** — `Install-PassReset.ps1` now accepts `-HostingMode IIS|Service|Console`. `IIS` is the default; `Service` registers the app as a Windows Service; `Console` runs the app in a plain console for development / diagnostics. *(installer, web)*
- **Windows Service host** — `PassReset.Web` uses `Microsoft.Extensions.Hosting.WindowsServices` to run under SCM. The same `PassReset.exe` binary runs under IIS (via the ASP.NET Core Module), as a Windows Service, or as a console app. *(web)*
- **`KestrelHttpsCertOptions`** — TLS configuration for Service mode. Supports cert-by-thumbprint (from the Windows cert store) OR cert-by-PFX-file. Configured via `Kestrel:HttpsCert:Thumbprint` / `Kestrel:HttpsCert:PfxPath` / `Kestrel:HttpsCert:PfxPassword` in `appsettings.Production.json`. *(web)*
- **Installer preflight** — cert resolution + port availability + service-account check run before any IIS / Service changes. Fails fast with actionable diagnostics; IIS is not touched on failed migration. New installer params: `-ServiceAccount`, `-PfxPath`, `-PfxPassword`. *(installer)*
- **Service-aware uninstaller** — `Uninstall-PassReset.ps1` now detects and removes the `PassReset` Windows Service before the IIS teardown, checks `sc.exe` exit codes, and warns on dual IIS+Service presence. *(installer)*
- **Pester test scaffold** — first Pester test file in the repo (`deploy/Install-PassReset.Tests.ps1`) covering installer parameters and preflight behavior. *(test, installer)*

### Configuration

- `PasswordChangeOptions.LocalPolicy.BannedWordsPath` (null default) — banned-words file path
- `PasswordChangeOptions.LocalPolicy.LocalPwnedPasswordsPath` (null default) — HIBP corpus directory
- `PasswordChangeOptions.LocalPolicy.MinBannedTermLength` (default: 4)
- `AdminSettings.Enabled` (default: `false`, opt-in) — master flag for the admin UI
- `AdminSettings.LoopbackPort` (default: `5010`)
- `AdminSettings.KeyStorePath` (default: `<install-dir>/keys`)
- `AdminSettings.DataProtectionCertThumbprint` (Linux only)
- `AdminSettings.AppSettingsFilePath` / `AdminSettings.SecretsFilePath` — overridable file paths
- `Kestrel:HttpsCert:Thumbprint` (null default) — Service-mode TLS cert from Windows cert store
- `Kestrel:HttpsCert:PfxPath` / `Kestrel:HttpsCert:PfxPassword` (null default) — Service-mode TLS via PFX file

### Changed

- **`PassReset.Web` retargeted to `net10.0`** (Phase 11) — the Windows provider is now a conditional `ProjectReference` gated on `$(OS) == 'Windows_NT'` with a `WINDOWS_PROVIDER` compile constant. Linux / Docker builds skip the Windows provider entirely. *(web)*
- **`PassReset.Tests` retargeted to `net10.0`** (Phase 11) — cross-platform tests run on Linux CI; Windows-only tests moved to `PassReset.Tests.Windows` (`net10.0-windows`). *(test)*
- **`HealthController` no longer references `System.DirectoryServices.AccountManagement`** — AD reachability is delegated to the DI-injected `IAdConnectivityProbe`. *(web)*

### Security

- **Admin endpoint isolation** — socket-level loopback binding (`127.0.0.1:<LoopbackPort>`) makes admin endpoints unreachable from the public listener.
- **Data Protection purpose isolation** — `PassReset.Configuration.v1` prevents cross-use of secret ciphertext with other DP consumers (antiforgery, etc.).
- **Antiforgery tokens** required on all admin POSTs.
- **Key-ring ACL** — installer creates `<install-dir>/keys` with a restrictive NTFS ACL (app pool: Modify; Administrators: FullControl; inheritance disabled).
- **Installer preflight** fails safely — IIS is not touched on failed Service-mode migration.

### Non-changes (explicit)

- **Windows provider unchanged.** `PassReset.PasswordProvider` (net10.0-windows) is byte-for-byte identical to v1.4.2. Zero regression for Windows operators.
- **`UserCannotChangePassword` ACE check** is deferred on the LDAP provider. AD's server-side modify rejection provides enforcement without the ACE check; the error message is less specific on Linux but behavior is correct.
- **`PassReset.Web` still uses the Phase-11 conditional TFM.** Cross-platform deployment of the web host remains blocked by NU1201: NuGet refuses to restore a plain `net10.0` project with a `ProjectReference` to a `net10.0-windows` one, even behind a `<Condition>` guard. Unblocking Linux hosting requires multi-targeting `PassReset.PasswordProvider` (deferred to a follow-up phase).
- **Windows contract tests are gone, not fixed.** The Phase-11 `PasswordChangeProviderContractTests` skip shim was deleted rather than unskipped. Reason: `UserPrincipal` is sealed in the .NET 10 BCL; `IPrincipalContextFactory` cannot return mockable principals. Windows provider retains its 139 impl-specific tests + Samba integration coverage; LDAP provider's 7 contract tests are unchanged.

### Known Limitations

- **Linux deployment remains blocked.** Alpha.1 retains the conditional TFM on `PassReset.Web` (see Non-changes). **Current alpha audience: Windows operators who want to test the new LDAP provider, local policy, admin UI, and Service-hosting pathways before the beta.**
- **Admin UI under IIS hosting is unverified.** Task 20 of the Phase 14 plan (IIS + admin UI smoke test) is deferred to this alpha's field testing. If the admin UI fails to render under IIS, a follow-up gate will disable the admin listener in IIS mode (use Service mode to access the admin UI in that case).

### Breaking

- None for Windows upgraders running with default config. The `ProviderMode` schema addition defaults to `Auto` (picks Windows provider on Windows); `AdminSettings.Enabled` defaults to `false`; `-HostingMode` defaults to `IIS` (matching v1.x behavior).

---

## [1.4.2] — 2026-04-20

Installer hotfix rolling up four PS 7 compatibility issues that blocked `Install-PassReset.ps1` on clean Windows Server hosts. No behavior change in the running application.

### Fixed
- **Cryptic "drive IIS not found" error:** `Import-Module WebAdministration -ErrorAction SilentlyContinue` silently swallowed failures, letting the installer stumble into a `Cannot find drive. A drive with the name "IIS" does not exist` error ~200 lines later (Windows' exception formatter renders the drive name as `"ISS"`, looking like a typo). Replaced with `-ErrorAction Stop` inside a try/catch that aborts with a clear "run DISM to install IIS Management Scripts and Tools" message. *(installer)*
- **PS 7 WinPSCompat warning noise:** Every install now suppresses the `Module WebAdministration is loaded in Windows PowerShell using WinPSCompatSession remoting session…` warning via `-WarningAction SilentlyContinue`. The warning is informational only; the WinPSCompat path is the only way PS 7 can load the legacy PSSnapIn-based `WebAdministration` module. *(installer)*
- **IIS:\ PSDrive missing even with IIS Management Scripts and Tools installed:** On PS 7, `Import-Module WebAdministration` registers the `IIS:\` PSDrive *inside* the WinPSCompat remoting session, not in the local PS 7 session where `Set-ItemProperty` runs. Downstream calls failed with `Cannot find drive`. Installer now detects the missing drive with `Get-PSDrive` and explicitly registers it via `New-PSDrive -PSProvider WebAdministration -Root 'MACHINE/WEBROOT/APPHOST' -Scope Script`. If registration fails, abort with actionable diagnostics including a PS 5.1 fallback recommendation. *(installer)*

---

## [1.4.1] — 2026-04-20

Hotfix for a flaky test that blocked the v1.4.0 CI release gate. No behavior change.

### Fixed
- **CI release gate:** Removed `SmtpProbeTests.ConnectAsync_RespectsCancellationToken`. The test asserted a 3s `CancellationToken` would fire before any OS timeout while connecting to a RFC 5737 blackhole address, but GitHub Actions `windows-latest` runners raise a TCP SYN-retry `SocketException` at ~9s *before* the CTS fires, breaking the 4s assertion. The SMTP probe invariant ("must not hang an IIS worker thread") is already covered end-to-end by `HealthControllerTests.Get_ReturnsUnhealthy_WhenSmtpUnreachable`, which exercises the full controller against a TEST-NET-1 target. The deleted unit test was redundant belt-and-suspenders coverage that picked a platform-dependent network assertion. *(test, web)*

---

## [1.4.0] — 2026-04-20

Stabilization milestone rolling up 21 post-v1.3.2 GitHub issues across installer, configuration, security, and operational readiness. Requires **PowerShell 7+** for install/publish scripts.

### Added
- **STAB-018 (gh#31):** `GET /api/health` now returns a nested `checks` object with per-dependency readiness for `ad`, `smtp`, and `expiryService` — each with `status`, `latency_ms`, and `last_checked`. Aggregate status rolls up (`unhealthy` > `degraded` > `healthy`); HTTP 200 when healthy, 503 otherwise. `smtp.skipped=true` when both `EmailNotificationSettings.Enabled` and `PasswordExpiryNotificationSettings.Enabled` are false; `expiryService.status="not-enabled"` when the background service isn't registered. Probes use `TcpClient.ConnectAsync` with a 3-second `CancellationTokenSource` deadline so the endpoint cannot hang an IIS worker thread. Response body is verified to contain no secrets (SMTP password, LDAP password, reCAPTCHA `PrivateKey`). New `IExpiryServiceDiagnostics` interface exposes `IsEnabled` + atomic `LastTickUtc`. *(web, security)*
- **STAB-019 (gh#34):** `Install-PassReset.ps1` now runs a post-deploy verification block after AppPool recycle — retries `GET /api/health` + `GET /api/password` up to 10× at 2s intervals (≈20s), hard-fails with `exit 1` and prints the last response body on failure. Prints `Health OK -- AD: <status>, SMTP: <status>, ExpiryService: <status>` on success. New `-SkipHealthCheck` switch (default `$false`) bypasses verification for air-gapped hosts. Under `-Force` the check still runs but does not prompt on failure. Operator UAT runbook shipped at `deploy/HUMAN-UAT-10-02.md`. *(installer)*
- **STAB-020 (gh#35):** New `security-audit` CI job in `.github/workflows/tests.yml` runs `npm audit --json` + `dotnet list src/PassReset.sln package --vulnerable --include-transitive` on every push and pull request, in parallel with the existing `tests` job. Fails the build on any **high** or **critical** finding not in the allowlist; **moderate** / **low** findings are surfaced in the job summary without failing. New `deploy/security-allowlist.json` documents intentional exceptions with mandatory `reason` + `expires` fields; `docs/Security-Audit-Allowlist.md` documents the add/renew workflow. *(ci, security)*
- **STAB-021 (gh#38):** The effective AD password policy is now visible by default — the `AdPasswordPolicyPanel` renders **above the Username field** so operators and users see the rules (min length, complexity, history, min/max age) before typing anything. New `ShowAdPasswordPolicy` setting defaults to `true` across `appsettings.json`, `appsettings.Production.template.json`, `appsettings.schema.json`, and `ClientSettings.cs` (config-schema-sync invariant preserved). A11y regression guards added: `role="region"` with accessible name, tab order traverses panel before Username, no disclosure widget. *(web, test)*
- **STAB-014:** Integration-test coverage for the POST `/api/password` rate-limit and reCAPTCHA branches. Each test uses a dedicated `WebApplicationFactory` subclass so the fixed-window rate-limiter partition is freshly constructed per test (prevents the 5-req/5-min budget leaking across tests). Rate limiter is proven to emit 429 on the 6th request within the window; per-factory partitions proven independent; reCAPTCHA-enabled path verified via real Google `siteverify` with the publicly documented always-fail test secret. *(web, test)*
- **STAB-015:** Structured audit events via RFC 5424 STRUCTURED-DATA. New `AuditEvent` allowlist DTO (no `Password`/`Token`/`Secret`/`PrivateKey`/`ApiKey` fields — compile-time redaction). New `ISiemService.LogEvent(AuditEvent)` overload and `SiemSyslogFormatter` overload that emit an SD-element containing `outcome`, `eventType`, `user`, `ip`, `traceId`, and optional `detail` SD-PARAMs. New `SiemSettings.Syslog.SdId` setting (default `passreset@32473`, validated as non-empty, ≤32 chars, no `=`/space/`]`/`"`). SIEM operators can now index security events natively without log parsing. Hot-path no-throw invariant preserved. *(web, security)*
- **STAB-016:** HSTS header emission regression tests plus a middleware wiring fix — the security-headers middleware now resolves `IOptions<WebSettings>` from `HttpContext.RequestServices` at request time instead of capturing `webSettings` by closure at startup. Enables `EnableHttpsRedirect` to be flipped by integration tests (and by future config hot-reload). Tests verify HSTS present (`max-age=31536000; includeSubDomains`, never `preload`) when enabled and absent when disabled. *(web, security, test)*
- **STAB-017:** Documented environment-variable and `dotnet user-secrets` workflow for the three production secrets (`SmtpSettings.Password`, `PasswordChangeOptions.ServiceAccountPassword`, `ClientSettings.Recaptcha.PrivateKey`) via ASP.NET Core's default `__` path delimiter (no custom `PASSRESET_` prefix). New integration test proves env-var binding precedence and that `[JsonIgnore]` on `Recaptcha.PrivateKey` holds regardless of config source. Updated `docs/Secret-Management.md`, `docs/IIS-Setup.md`, `docs/appsettings-Production.md`, `CONTRIBUTING.md`. Installer does NOT set these env vars — operators own secret injection. Full encrypted-at-rest solutions (DPAPI/Key Vault) remain scheduled for v2.0. *(docs, security, test)*
- **STAB-008 (gh#27):** Authoritative `appsettings.schema.json` (JSON Schema Draft 2020-12) defining every valid configuration key, type, default, and constraint. Ships in every release zip alongside `appsettings.Production.template.json`. CI now runs `Test-Json -SchemaFile` on every push / pull request — a schema-template mismatch fails the build with a JSON-path error annotation. *(web, ci, deploy)*
- **STAB-009 (gh#25):** Pre-flight configuration validation at install time (`Install-PassReset.ps1` runs `Test-Json -Path appsettings.Production.json -SchemaFile appsettings.schema.json` before any sync; failure halts install with an actionable field-path error) and at host startup (`IValidateOptions<T>` + `ValidateOnStart()` on all 7 options classes — `PasswordChangeOptions`, `WebSettings`, `SmtpSettings`, `SiemSettings`, `EmailNotificationSettings`, `PasswordExpiryNotificationSettings`, `ClientSettings`). Startup failures emit D-08 messages (`Section.Field: reason (got "value"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.`) with secret values redacted. *(web, installer)*
- **STAB-010 (gh#24):** Schema-driven additive-merge config sync on upgrade. The installer walks `appsettings.schema.json` as the source of truth, adds missing keys from documented schema defaults, never modifies operator-set values, and treats arrays atomically (never merges array contents). Schema entries without a `default` (secrets, localized strings, environment-specific regexes) emit a warning instead of writing a placeholder. *(installer)*
- **STAB-011 (gh#26):** New `-ConfigSync <Merge|Review|None>` installer parameter. Interactive upgrades prompt `Config sync: [M]erge additions / [R]eview each / [S]kip? [M]`. Under `-Force` with no explicit value, resolves to `Merge`. Fresh installs resolve to `None` silently (template is copied fresh). Review mode prompts per-key for each missing or obsolete key. *(installer)*
- Windows Event Log source `PassReset` registered by `Install-PassReset.ps1` during prerequisites (idempotent; requires admin which the installer already asserts). The ASP.NET Core host writes configuration validation failures under this source at **event ID 1001** before re-throwing — 502 diagnostics now live in Event Viewer → Application log filtered by source `PassReset`. *(installer, web)*

### Changed
- **STAB-007 (gh#22):** `appsettings.Production.template.json` is now pure JSON. Inline `//` comments (which broke `Test-Json`, `System.Text.Json`, and any other strict JSON consumer) have been stripped. All operator-facing notes previously inline in the template are preserved verbatim in [`docs/appsettings-Production.md`](docs/appsettings-Production.md#section-notes-formerly-inline-in-template) under the "Section notes (formerly inline in template)" section. *(web, docs)*
- **STAB-012 (gh#37):** Schema-drift check rewritten to consume `appsettings.schema.json` as the source of truth (previously diffed template vs live config, which silently skipped whenever the live config parsed successfully). Drift check now runs unconditionally on every upgrade after sync, reporting missing required keys (with schema default where available), obsolete keys (flagged via `x-passreset-obsolete: true`), and unknown top-level keys. Diagnostic only — the check never mutates the file. *(installer)*
- `Install-PassReset.ps1` and `Publish-PassReset.ps1` now require **PowerShell 7+** (`Test-Json -SchemaFile` is unavailable in Windows PowerShell 5.1). Invoke via `pwsh -File .\Install-PassReset.ps1`. Running under `powershell.exe` fails at the `#Requires -Version 7.0` line — no partial install is possible. See [`UPGRADING.md`](UPGRADING.md#v140--configuration-schema-and-sync). *(installer, deploy)*

### Fixed
- **STAB-001 (gh#19):** Fresh installs no longer fail when IIS Default Web Site is bound to port 80. `Install-PassReset.ps1` now detects the conflict and offers three interactive choices (stop conflicting site(s), pick first free port in 8080-8090, or abort). `-Force` mode never silently stops a foreign site — it picks the alternate port and aborts only if 80 + 8080-8090 are all taken. Installer prints the final reachable URL(s) at completion. *(installer)*
- **STAB-002 (gh#20):** Re-running the installer with the currently installed version now prompts **"Re-configure existing installation?"** instead of the old "upgrade" prompt. On confirm the robocopy `/MIR` file mirror is skipped (existing publish folder preserved) while app-pool, binding, and config logic still re-run. `-Force` logs `-Force specified - re-configuring without file mirror`. *(installer)*
- **STAB-003 (gh#23):** Upgrade no longer prints a spurious `Could not read existing AppPool identity` warning before falling back to defaults. AppPool identity is now read via `Get-WebConfigurationProperty`, which is reliable across Windows PowerShell 5.1 and PowerShell 7.x. Restores the BUG-003 four-branch preserve contract byte-for-byte. *(installer)*
- **STAB-004 (gh#36):** Two consecutive password changes for the same user no longer surface as `UnauthorizedAccessException` / `E_ACCESSDENIED` / generic crash. A new `PreCheckMinPwdAge` helper compares `pwdLastSet` against the domain `minPwdAge` and short-circuits with `ApiErrorCode.PasswordTooRecentlyChanged` (code 19) and a minute-level remaining-time message. The existing `COMException` catch remains intact as the defense-in-depth floor. *(provider)*
- **STAB-005 (gh#39):** `Uninstall-PassReset.ps1` now parses and runs cleanly on Windows PowerShell 5.1 and PowerShell 7.x. Re-saved as UTF-8 with BOM; Unicode `─` (U+2500) box-drawing dividers replaced with ASCII `---`. `-KeepFiles` continues to preserve the publish folder. *(deploy)*
- **STAB-006 (gh#21):** `Install-PassReset.ps1` now offers a single Y/N consent prompt to enable all missing IIS features via DISM (`Start-Process dism.exe /online /enable-feature`), gated by `$PSCmdlet.ShouldProcess` so `-WhatIf` works. Declining the prompt prints the exact DISM commands to run manually and exits 0 cleanly. A missing or wrong-version .NET 10 Hosting Bundle no longer aborts — it prints the download URL (`https://dotnet.microsoft.com/download/dotnet/10.0`) and exits 0. *(installer)*

### Security
- **STAB-013:** POST `/api/password` now collapses `InvalidCredentials` and `UserNotFound` error codes to `ApiErrorCode.Generic` (0) on the wire in Production to resist account-enumeration attacks. SIEM emission remains granular (`MapErrorCodeToSiemEvent` is unchanged), so SOC operators can still triage the two failure modes from syslog alone. Development and Test environments preserve specific codes for debugging (D-03 regression guard). Decision gate is `IHostEnvironment.IsProduction()` at the controller edge. *(web, security)*

---

## [1.3.2] — 2026-04-16

Patch release rolling up the post-v1.3.1 deep code review fixes. No user-visible behavior changes; all changes are internal diagnostic-path hardening on top of v1.3.1.

### Fixed
- **`ExceptionChainLogger`** (WR-01): added cycle protection and a depth bound to the `InnerException` walker so a malformed or adversarial exception chain (self-referencing or pathologically deep) can no longer spin indefinitely or bloat a single log event.
- **`PasswordChangeProvider`** (WR-01): aligned the COM/`DirectoryServicesCOMException` error log template placeholder count with its named-argument list to stop Serilog's structured renderer from dropping the `ExceptionChain` property on that path.
- **`LockoutPasswordChangeProvider`** (WR-03): removed a redundant `Debug` log emitted from inside `IncrementCounter` on every attempt — the outer call site already logs the resulting counter state as `Warning`, so the inner entry only duplicated output without adding context.

### Testing
- `PasswordLogRedactionTests`: renamed the misleading redaction tests so their names reflect what they actually assert (sentinel-plaintext on the exception-log path), and added a direct `ExceptionChainLogger` sentinel test that confirms AD-supplied `Exception.Message` passthrough is bounded by the documented accepted-risk decision.

---

## [1.3.1] — 2026-04-15

Diagnostic patch release. No user-visible behavior changes. Existing `appsettings.Production.json` continues to work unchanged; operators can flip `Serilog:MinimumLevel:Default` to `Debug` to enable the new step-granular diagnostics.

### Added
- **Structured AD diagnostics** (BUG-004): every password-change request now correlates via W3C `Activity.TraceId` (pushed as a Serilog `LogContext` property by a new `TraceIdEnricherMiddleware`). `PasswordController.PostAsync` opens a request-scoped `BeginScope` with `Username`, `TraceId`, and `ClientIp`. `PasswordChangeProvider.PerformPasswordChangeAsync` opens a nested AD-context scope (`Domain`, `DomainController`, `IdentityType`, `UserCannotChangePassword`, `LastPasswordSetUtc`) once the user principal is resolved, and emits `Debug` step-before/after events (with elapsed milliseconds) around user lookup, `ChangePasswordInternal`, and `Save`. `LockoutPasswordChangeProvider` adds `Debug` events for counter increments and eviction sweeps; existing `Warning` logs for `ApproachingLockout` / `PortalLockout` are preserved.
- **`ExceptionChainLogger`** helper: for `DirectoryServicesCOMException` and `PasswordException`, walks `InnerException` and emits a structured `ExceptionChain` property — an array of `{depth, type, hresult, message}` — so operators can diagnose intermittent `0x80070005 (E_ACCESSDENIED)` and related failures without repro-in-debugger. `PrincipalOperationException` gets its own targeted catch with default Serilog exception destructure (no chain walker).

### Testing
- `ExceptionChainLoggerTests` — unit tests for both exception types using a handwritten `ListLogEventSink` (no new Serilog test packages).
- `PasswordLogRedactionTests` — sentinel-plaintext tests across `PasswordChangeProvider`, `LockoutPasswordChangeProvider`, and end-to-end via `WebApplicationFactory`. Asserts no known plaintext passwords ever appear in any rendered message or property value.

### Security
- Plaintext passwords provably never reach log output — enforced by `PasswordLogRedactionTests` as a CI gate.

---

## [1.3.0] — 2026-04-15

Feature release adding four opt-in UX improvements plus the automated test foundation. All new settings default off / fail-closed, so the v1.2.3 behavior is preserved when operators omit the new config blocks.

### Added
- **Operator branding** (FEAT-001): `BrandingSettings` with 8 nullable fields (company name, portal name, helpdesk URL/email, usage text, logo, favicon, asset root). New `/brand/*` static-file route served from `%ProgramData%\PassReset\brand\` via `PhysicalFileProvider` (`ServeUnknownFileTypes=false`). Upgrade-safe installer provisioning. `BrandHeader` component with icon fallback, runtime favicon injection, helpdesk block, usage text override, footer company-name override.
- **AD password policy panel** (FEAT-002): `PasswordPolicy` record in `PassReset.Common`, RootDSE-based query for `minPwdLength` / `pwdProperties` / `pwdHistoryLength` / `minPwdAge` / `maxPwdAge`, `PasswordPolicyCache` wrapping `IMemoryCache` (1h success / 60s failure TTL keyed by domain DN), new `GET /api/password/policy` endpoint (200 or 404 when disabled/unavailable), `AdPasswordPolicyPanel` + `usePolicy()` hook. Hidden unless `ShowAdPasswordPolicy: true`, fails closed on null.
- **Clipboard auto-clear** (FEAT-003): `ClipboardClearSeconds` setting (default 30, `0` disables). `scheduleClipboardClear` helper with readback guard — clipboard is only cleared if it still contains the generated password. `ClipboardCountdown` chip with warning color at ≤5s and a 2s "cleared" confirmation. Regenerating cancels the previous timer; submit also cancels. Silent no-op when `navigator.clipboard` is unavailable.
- **HIBP pre-check on blur** (FEAT-004): Client-side SHA-1 via WebCrypto; only the 5-char hex prefix leaves the browser (k-anonymity). New `POST /api/password/pwned-check` endpoint proxies the HIBP range API and returns `{ suffixes, unavailable }`; client matches the full-hash suffix locally. New rate-limit policy `pwned-check-window` (20 req / 5 min per IP) with SIEM event on rejection. `AbortController` cancels in-flight requests. Server default remains fail-closed (`FailOpenOnPwnedCheckUnavailable: false`); endpoint is additive with no breaking changes to existing routes.
- Automated test foundation: xUnit v3 backend suite (LockoutPasswordChangeProvider,
  PwnedPasswordChecker, ApiErrorCode mapping incl PasswordTooRecentlyChanged,
  SiemSyslogFormatter, Levenshtein, ChangePasswordModel validation, and PasswordController
  integration via WebApplicationFactory) with coverlet.msbuild thresholds enforced
  (20% line / 20% branch baseline — raised over time as coverage grows). (QA-001)
- Automated frontend test suite: Vitest + React Testing Library covering PasswordForm,
  PasswordStrengthMeter, ErrorBoundary, useSettings, levenshtein, and passwordGenerator
  with v8 coverage thresholds (50% line / 40% branch). (QA-001)
- CI gate: new reusable `.github/workflows/tests.yml` called from `ci.yml` (after build)
  and `release.yml`; the release publish job is blocked on test failure via `needs: tests`,
  so a failing test prevents the release zip from being built and uploaded. (QA-001)

### Changed
- `PwnedPasswordChecker` converted from internal static to an instance class with
  injected `HttpClient` (DI) for testability, and now implements `IPwnedPasswordChecker` so the new pre-check endpoint can share the same range-fetch path. No behavior change to the existing in-flow breach check. (QA-001, FEAT-004)
- Extracted `SiemSyslogFormatter` (pure static helper) from `SiemService` so RFC 5424
  packet construction is testable without sockets. No behavior change. (QA-001)

### Security
- Added top-level `permissions: contents: read` to `release.yml` to scope the `GITHUB_TOKEN` used by the called `tests.yml` workflow (CodeQL `actions/missing-workflow-permissions`). The release job keeps its job-level `contents: write` override.
- `PwnedPasswordChecker.FetchRangeAsync` now guards its input with a compiled `^[0-9A-Fa-f]{5}$` regex and omits the user-supplied prefix from exception log entries, eliminating the `cs/log-forging` taint path at the sink.

---

## [1.2.3] — 2026-04-14

Bug-fix release. Three operator-visible fixes — SMTP with internal CAs, clearer UX when AD blocks a too-recent password change, and installer preservation of the IIS AppPool identity on upgrade.

### Fixed
- **SMTP**: Internal-CA-issued relay certificates can now be trusted via an opt-in
  `SmtpSettings.TrustedCertificateThumbprints` allowlist (SHA-1 or SHA-256). No silent
  bypass — entries must be explicitly configured. See `docs/appsettings-Production.md`.
  (BUG-001)
- **Installer**: `Install-PassReset.ps1` now preserves the existing IIS
  AppPool identity on upgrade. Previously, running the installer without
  `-AppPoolIdentity` would reset a manually-configured service account
  back to `ApplicationPoolIdentity`. Fresh-install default and explicit
  `-AppPoolIdentity` override behaviour are unchanged. See `UPGRADING.md`.
  (BUG-003)
- **Error handling**: Password changes rejected by the domain's `minPwdAge`
  (AD HRESULT `0x80070005`) now surface as a dedicated
  `ApiErrorCode.PasswordTooRecentlyChanged` with a localized user message
  instead of a generic "Unexpected Error". The mapping is narrow — genuine
  access-denied cases from missing service-account rights are logged with
  a remediation hint rather than misclassified. Alert copy configurable
  via `ClientSettings.Alerts.ErrorPasswordTooRecentlyChanged`. (BUG-002)

---

## [1.2.2] — 2026-04-14

Installer hardening release. No application code changes — upgrade path only.

### Fixed
- **`appsettings.Production.json` is now preserved across upgrades.** `robocopy /MIR` previously deleted the operator's live production config on every upgrade (the backup saved it, but the live site ran off the template copy until manually restored). `/XF` exclusions now protect `appsettings.Production.json` and `appsettings.Local.json`; a `logs\` directory under the deploy root is also preserved via `/XD`.

### Added
- **Downgrade detection.** `Install-PassReset.ps1` parses installed vs. incoming versions as `[version]` and warns in red when the incoming build is older than the installed one; the confirmation prompt changes to "Continue with DOWNGRADE?" and `-Force` emits a warning rather than silent acceptance.
- **Backup retention.** The installer now keeps the 3 most recent `*_backup_*` folders and prunes older ones automatically to prevent unbounded disk use on servers with frequent upgrades.
- **Auto-rollback on startup failure.** `Start-WebAppPool` / `Start-Website` are wrapped in try/catch with a 3-second settle delay and explicit state verification. If the worker fails to start after an upgrade, the installer mirrors the backup back and restarts the site; if rollback itself fails, it aborts with manual-recovery instructions.
- **Config schema drift warning.** After a successful upgrade, the installer diffs the key paths in `appsettings.Production.template.json` against the live `appsettings.Production.json` and lists any new template keys the operator should add manually. No auto-merge — too risky with nested or array values.

---

## [1.2.1] — 2026-04-14

Dependency and security maintenance release. No behavior or configuration changes.

### Changed
- **Frontend build migrated to Vite 8.** Vite 8.0.8 uses rolldown instead of Rollup as the underlying bundler. Build output is functionally equivalent; the produced JS bundles differ slightly in chunking. `@vitejs/plugin-react` bumped to 6.0.1.
- **Grouped npm minor/patch updates** (ESLint plugins, typescript-eslint, etc.) applied via Dependabot.
- **NuGet packages updated**: Serilog.AspNetCore and Serilog.Sinks.File bumped to latest stable.
- **GitHub Actions bumped**: `actions/checkout@v6.0.2`, `actions/setup-node@v6.3.0`, `actions/setup-dotnet@v5.2.0`.

### Security
- **Vite patched to 6.4.2 → 8.0.8**, resolving CVE advisories for arbitrary file read via dev-server WebSocket and path traversal in optimized deps `.map` handling.
- **CI workflow hardened**: `GITHUB_TOKEN` permissions restricted to `contents: read` (least privilege).
- **Repository rulesets added**: `master` branch protection (status checks, linear history, no force-push, no deletion) and `v*` tag immutability.
- **Dependabot enabled** for npm, NuGet, and GitHub Actions with weekly grouped update PRs. Pre-release versions (TypeScript 6 beta, MUI v7+ beta, ESLint 10) are explicitly ignored until upstream stability lands.

### Deferred
- ESLint 10 upgrade waits on `eslint-plugin-react-hooks` shipping a stable with ESLint 10 peer support.
- TypeScript 6 and MUI v7+ majors remain on pre-release channels upstream and are held back by Dependabot ignore rules.

---

## [1.2.0] — 2026-04-13

### Breaking Changes

- **Group check fallback now denies by default.** When both `GetGroups()` and `GetAuthorizationGroups()` fail for a user, the portal now returns `ChangeNotPermitted` instead of allowing the password change. Set `PasswordChangeOptions.AllowOnGroupCheckFailure` to `true` to restore the previous behavior.
- **reCAPTCHA score threshold is now configurable.** The hardcoded `0.5` threshold has been replaced by `Recaptcha.ScoreThreshold` (default: `0.5`). Existing behavior is unchanged unless you modify this setting.

### Added

- **File logging via Serilog**: Errors and warnings are written with full structured context (exception, stack trace, scope properties); info/success lines are terse. Logs go to `%SystemDrive%\inetpub\logs\PassReset\passreset-YYYYMMDD.log` (IIS convention, outside wwwroot so files are not web-accessible). Daily rolling, 30-day retention, 10 MB per-file cap, shared write mode for multi-worker app pools. Usernames and client IPs are logged; passwords are never logged. HTTP request summaries are logged via `UseSerilogRequestLogging`.
- **Installer log folder + ACL**: `Install-PassReset.ps1` now creates `%SystemDrive%\inetpub\logs\PassReset` and grants `Modify` to the app pool identity (previously created `<site>\logs` under wwwroot).
- Startup configuration validator checks for incompatible flag combinations. Error-level issues (debug provider in production, reCAPTCHA enabled without key) abort startup. Warning-level issues (email enabled without SMTP, high lockout threshold) are logged.
- `Recaptcha.FailOpenOnUnavailable` option (default: `false`) distinguishes reCAPTCHA service unavailability from score failure. When enabled, network errors allow the request through; low scores still reject.
- `Recaptcha.ScoreThreshold` option (default: `0.5`) makes the reCAPTCHA v3 acceptance threshold configurable.
- `PasswordChangeOptions.AllowOnGroupCheckFailure` option (default: `false`) controls behavior when AD group checks fail.
- Email retry with exponential backoff (3 attempts: 1s, 10s, 60s) in SmtpEmailService. Permanent SMTP errors (auth, recipient rejection) are not retried.
- Syslog `ipAddress` parameter escaped via `EscapeSd()` for defense in depth against header spoofing.
- Lockout dictionary bounded at 10,000 entries. Oldest 25% evicted when cap is exceeded.
- `/api/health` response includes `lockout.activeEntries` count for monitoring.
- Password expiry notification group enumeration runs in parallel with SemaphoreSlim(5) cap.

### Changed

- Migrated frontend password strength meter from `zxcvbn` (unmaintained since 2017) to `@zxcvbn-ts/core`. Scoring behavior is preserved.

---

## [1.1.1] — 2026-04-01

### Added
- **EXE version stamping**: `PassReset.Web.exe` now embeds the release version in its file details (`FileVersion` / `ProductVersion`). The installer reads this for upgrade version comparison.
- **Production config template validation**: `Publish-PassReset.ps1` now compares all config keys in `appsettings.json` against `deploy/appsettings.Production.template.json` and **fails the build** if any keys are missing — prevents shipping releases with an incomplete production config template.
- **`deploy/appsettings.Production.template.json`**: standalone template file replaces the inline config that was previously hardcoded in the installer script. Single source of truth for the starter production config.
- **Installer secret parameters**: `Install-PassReset.ps1` accepts `-LdapPassword`, `-SmtpPassword`, and `-RecaptchaPrivateKey` (all `SecureString`). Secrets are stored as IIS app pool environment variables, scoped to the pool — never written to `appsettings.Production.json`. Existing values are preserved on upgrade.
- **Dark mode screenshot**: `docs/screenshot-dark.png` added; README uses `<picture>` element for automatic light/dark switching on GitHub.

### Changed
- **Installer config generation**: replaced ~125 lines of inline `PSCustomObject` with a file copy from the shipped template. Template is validated at build time, so it can never fall out of sync.
- **Upgrade version display**: installer now reads `FileVersion` (clean `1.1.0`) instead of `ProductVersion` (includes git hash suffix).
- **GitHub URLs**: all references updated from `phibu/passreset` to `phibu/AD-Passreset-Portal`.

### Docs
- `appsettings-Production.md`: added `FailOpenOnPwnedCheckUnavailable` and `AllowSetPasswordFallback` to the PasswordChangeOptions reference table.
- `IIS-Setup.md`: updated Step 6 example with `-LdapPassword` / `-SmtpPassword` parameters; added environment variable note to installer feature list.
- Updated screenshots to reflect current teal theme, lock icon header, and footer.

---

## [1.1.0] — 2026-03-29

### Added
- **Async provider chain**: `IPasswordChangeProvider.PerformPasswordChange` is now `PerformPasswordChangeAsync`. The HIBP breach check uses async `HttpClient.GetAsync` instead of blocking `Send`, eliminating thread pool pressure under concurrent load.
- **React Error Boundary**: unhandled React rendering errors now show a user-friendly fallback with a reload button instead of a white screen.
- **Dark mode**: automatic light/dark theme switching via `prefers-color-scheme` media query detection.
- **ESLint + Prettier**: frontend linting and formatting with `npm run lint` and `npm run format:check` scripts.
- **Loading skeleton**: replaced the spinner-only loading state with skeleton placeholders that preserve layout and minimise CLS.
- **`aria-live` region**: screen readers now announce dynamic error messages and lockout warnings.
- **SPA fallback route**: `MapFallbackToFile("index.html")` ensures direct navigation to non-root paths serves the app.
- **Multi-DC health check**: `GET /api/health` now probes all configured LDAP hostnames (not just the first) and returns 503 with per-check details when AD is unreachable.
- **`FailOpenOnPwnedCheckUnavailable`** config option: when `true`, HIBP API outages skip the breach check with a warning log instead of blocking all password changes (default: `false`).
- **`AllowSetPasswordFallback`** config option: opt-in for the administrative `SetPassword` fallback on COMException (default: `false`; bypasses AD password history when enabled).
- **`SECURITY.md`**: responsible disclosure policy with scope, response timeline, and security architecture summary.
- **`docs/Known-Limitations.md`**: 15 documented constraints covering platform, deployment, authentication, password policy, networking, monitoring, and frontend.
- **`docs/Secret-Management.md`**: expanded with IIS environment variable PowerShell commands, credential rotation procedure, and file permission verification.

### Changed
- **Primary color**: teal darkened from `#0d7377` to `#0b6366` for WCAG AA contrast compliance (~4.7:1 on white).
- **NuGet packages**: updated `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `System.DirectoryServices`, and `System.DirectoryServices.AccountManagement` from preview/9.0.0 to stable 10.0.5.
- **Inter font**: weight 700 now loaded from Google Fonts (used by the product name header).

### Fixed
- **Rate limiter**: converted from a global bucket (all users shared 5 req/5 min) to per-IP partitioned policy using `RateLimitPartition.GetFixedWindowLimiter`.
- **Lockout dictionary memory leak**: added `Timer`-based eviction of expired entries every 5 minutes; `LockoutPasswordChangeProvider` now implements `IDisposable`.
- **`SetPassword` fallback**: now opt-in via `AllowSetPasswordFallback` (was unconditional when `UseAutomaticContext=false`). Prevents accidental bypass of AD password history enforcement.
- **Non-JSON error handling**: `changePassword()` client now checks `Content-Type` header before calling `res.json()`, preventing crashes on HTML error pages (502, proxy errors).
- **CSP hardening**: added `base-uri 'self'`, `form-action 'self'`, `object-src 'none'` directives.
- **Request size limit**: added `[RequestSizeLimit(8192)]` on the POST endpoint (was unbounded at 30MB default).
- **Model validation**: added `[MaxLength(256)]` on `NewPasswordVerify` and `[MaxLength(2048)]` on `Recaptcha` field.
- **Syslog injection**: `EscapeSd()` now strips control characters (U+0000–U+001F, U+007F) in addition to escaping RFC 5424 special characters.
- **reCAPTCHA logging**: validation exceptions now logged at Warning level instead of silently swallowed.
- **DNS refresh**: static `HttpClient` instances for HIBP and reCAPTCHA now use `SocketsHttpHandler` with `PooledConnectionLifetime` (10 min).
- **`document.title`**: moved from render body to `useEffect` to fix React StrictMode side effect.
- **Password generator**: replaced modulo bias with rejection sampling for uniform random index.
- **Syslog connections**: TCP/UDP clients now pooled with lazy init and reconnect instead of creating a new connection per event.
- **Health endpoint**: removed version info to limit fingerprinting; added AD connectivity probing.
- **`fetchSettings`**: now checks `Content-Type` header before parsing JSON.

### Docs
- Fixed health endpoint path in `README.MD` and `IIS-Setup.md` (`/health` → `/api/health`).
- Added Security section and Known Limitations link to `README.MD`.
- Added new docs to project structure in `README.MD`.

---

## [1.0.5] — 2026-03-28

### Added
- **Configurable notification email strategy** (`PasswordChangeOptions.NotificationEmailStrategy`): choose how the recipient address is resolved for password-changed emails — `Mail` (AD mail attribute, default), `UserPrincipalName`, `SamAccountNameAtDomain` (`{sam}@{NotificationEmailDomain}`), or `Custom` (template string with `{samaccountname}`, `{userprincipalname}`, `{mail}`, `{defaultdomain}` placeholders).
- **SIEM integration** (`SiemSettings`): security events forwarded via RFC 5424 syslog (UDP or TCP) and/or email alerts. Syslog channel is fully configurable (host, port, protocol, facility, app name). Email alert channel reuses existing SMTP settings and fires on configurable event types (`AlertOnEvents`). Events covered: `PasswordChanged`, `InvalidCredentials`, `UserNotFound`, `PortalLockout`, `ApproachingLockout`, `RateLimitExceeded`, `RecaptchaFailed`, `ChangeNotPermitted`, `ValidationFailed`, `Generic`.

---

## [1.0.4] — 2026-03-27

### Added
- `Uninstall-PassReset.ps1`: removes the IIS site, app pool, and deployment folder created by the installer. Supports `-KeepFiles` (preserve files for reinstall), `-RemoveBackups` (also delete upgrade backup folders), and `-Force` (unattended). IIS, IIS features, the .NET Hosting Bundle, and certificates are not touched.
- `Uninstall-PassReset.ps1` is now included in the release zip alongside `Install-PassReset.ps1`.

### Docs
- `AD-ServiceAccount-Setup.md`: replaced single-OU `dsacls` examples with a reusable `$ous` array + `foreach` loop across all delegation steps (Option A Steps 2–3, Option B Step 5). Added tip for delegating at a parent OU level when all users share a common OU tree.

---

## [1.0.3] — 2026-03-27

### Added
- `Install-PassReset.ps1`: upgrade detection — shows installed vs incoming version, prompts for confirmation (`Y/N`), creates a dated backup of the current deployment before overwriting (e.g. `PassReset_backup_20260327-1430\`).
- `Install-PassReset.ps1`: `-Force` switch skips the upgrade confirmation prompt for unattended/CI deployments.

### Fixed
- `LockoutPasswordChangeProvider`: replaced `IMemoryCache` with `ConcurrentDictionary` — eliminates CI build error (`IMemoryCache` namespace not found in class library), fixes a race condition in the failure counter (non-atomic read-modify-write), corrects an off-by-one (approaching-lockout warning now fires on the attempt that hits the threshold, not one too early), and makes the lockout window absolute rather than sliding.
- `Install-PassReset.ps1`: `Remove-WebBinding` now pipes the binding object instead of passing named parameters — fixes "Cannot find binding '\*:443:\*'" error when upgrading over an existing installation.
- `Install-PassReset.ps1`: starter `appsettings.Production.json` now includes all current config keys: `AllowedUsernameAttributes`, `PortalLockoutThreshold`, `PortalLockoutWindow`, `ValidationRegex`, `ChangePasswordForm`, `ErrorsPasswordForm`, and the full `Alerts` section.

### Docs
- `appsettings-Production.md`: added `AllowedUsernameAttributes`, `PortalLockoutThreshold`, `PortalLockoutWindow` to PasswordChangeOptions; added full `AllowedUsernameAttributes`, `ValidationRegex`, `ChangePasswordForm`, `ErrorsPasswordForm`, and `Alerts` sections to ClientSettings.
- `IIS-Setup.md`: updated Step 6 with upgrade instructions and `-Force` flag; updated Step 7 config example with new keys.

---

## [1.0.2] — 2026-03-27

### Added
- **Portal lockout counter** (`LockoutPasswordChangeProvider`): per-username in-memory failure counter blocks portal access after `PortalLockoutThreshold` consecutive wrong-password attempts for the configured `PortalLockoutWindow` duration. Prevents both self-lockout loops and targeted account lockout via AD.
- **Approaching-lockout warning**: the UI now shows a `warning` banner when one more failed attempt will trigger the portal block (`ApiErrorCode.ApproachingLockout = 18`).
- New `ApiErrorCode` values: `PortalLockout` (17) and `ApproachingLockout` (18).
- `PasswordChangeOptions`: `PortalLockoutThreshold` (default 3) and `PortalLockoutWindow` (default 30 min) configuration keys.
- `ClientSettings.Alerts`: `ErrorPortalLockout` and `ErrorApproachingLockout` configurable strings.

### Fixed
- `Install-PassReset.ps1`: HTTP :80 binding is now retained by default when HTTPS is configured so that `UseHttpsRedirection()` can issue 301 redirects. Pass `-HttpPort 0` for HTTPS-only (no redirect).
- `IIS-Setup.md`: Step 9 and new `ERR_CONNECTION_REFUSED` troubleshooting entry document the HTTP binding requirement.
- `Publish-PassReset.ps1`: Release zip now has correct structure (`Install-PassReset.ps1` at root, app files under `publish\`). Previously everything was flattened to the zip root.

---

## [1.0.1] — 2026-03-26

### Added
- LDAPS support: `LdapUseSsl` and `LdapPort` options in `PasswordChangeOptions`
- `IValidateOptions<PasswordChangeOptions>` startup validator (`PasswordChangeOptionsValidator`)
- `SafeAccessTokenHandle` for safe Win32 P/Invoke token cleanup in `NativeMethods`
- `bool?` tri-state return from `PwnedPasswordChecker` — distinguishes API failure from confirmed pwned
- `ApiErrorCode.PwnedPasswordCheckFailed` (16) for HIBP service unavailability
- `Recaptcha.Enabled` flag at all layers (config, C# model, controller, TypeScript, React)
- `errorPwnedPasswordCheckFailed` alert string in client settings
- `useMemo`-based safe regex construction in `PasswordForm.tsx`
- reCAPTCHA v3 action validation (`change_password`) in `PasswordController`
- `FormUrlEncodedContent` for reCAPTCHA secret key (no longer interpolated into URL)
- `appsettings-Production.md` — full reference documentation for all config sections
- GitHub Actions CI workflow (`.github/workflows/ci.yml`)
- GitHub Actions release workflow (`.github/workflows/release.yml`)
- `.editorconfig` for consistent line endings and indentation
- Conventional Commits `commit-msg` hook and `.gitmessage` template
- `CONTRIBUTING.md` — commit convention, branch naming, release workflow

### Changed
- `Install-PassReset.ps1`: `AppPoolPassword` parameter changed to `SecureString`; Marshal BSTR pattern for safe credential handling
- `Install-PassReset.ps1`: Starter config now generated via `PSCustomObject + ConvertTo-Json` (no more here-string with comments)
- `Install-PassReset.ps1`: Removed `Web-ASPNET45` / `Web-Asp-Net45` (not present on Server 2019+); updated synopsis to 2019/2022/2025
- `Install-PassReset.ps1`: Replaced Unicode glyphs with ASCII (`[>>]`, `[OK]`, `[!!]`, `[ERR]`)
- `Publish-PassReset.ps1`: Fixed `Compress-Archive` to include `Install-PassReset.ps1` via staging copy
- `PasswordController`: reCAPTCHA guard now checks `Enabled == true` before validating token
- `PasswordExpiryNotificationService`: daily dedup set now uses `Clear()` instead of date-filtered `RemoveWhere`
- `appsettings.json` (dev + publish): removed JSON comments; added `LdapPort`, `LdapUseSsl`, `Recaptcha`, `ErrorPwnedPasswordCheckFailed`
- `IIS-Setup.md`: updated for Server 2019/2022/2025; `Read-Host -AsSecureString` in example

### Fixed
- `PasswordChangeProvider`: `catch (COMException)` now captures HResult for structured logging
- `PasswordChangeProvider`: `AcquireDomainEntry` uses `LDAPS://` or `LDAP://` prefix correctly
- `PasswordChangeProvider`: `ValidateUserCredentials` disposes `SafeAccessTokenHandle` via `.Dispose()`

---

## [1.0.0] — 2025-03-25

### Added
- Initial PassReset release — complete .NET 10 + React 19 implementation
- Renamed from PassCore → ReKey → PassReset
- Three-project solution: `PassReset.Web`, `PassReset.PasswordProvider`, `PassReset.Common`
- IIS deployment scripts (`Install-PassReset.ps1`, `Publish-PassReset.ps1`)
