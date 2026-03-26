# Changelog

All notable changes to PassReset are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).
Versions follow [Semantic Versioning](https://semver.org/).

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

## [1.0.0] — 2025-03-xx

### Added
- Initial PassReset release — complete .NET 10 + React 19 implementation
- Renamed from PassCore → ReKey → PassReset
- Three-project solution: `PassReset.Web`, `PassReset.PasswordProvider`, `PassReset.Common`
- IIS deployment scripts (`Install-PassReset.ps1`, `Publish-PassReset.ps1`)
