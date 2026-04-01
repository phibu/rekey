# Changelog

All notable changes to PassReset are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/).

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
