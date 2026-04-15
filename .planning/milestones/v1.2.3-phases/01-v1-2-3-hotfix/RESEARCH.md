# Phase 1 Research — v1.2.3 Hotfix

**Researched:** 2026-04-14
**Domain:** ASP.NET Core 10 / MailKit / System.DirectoryServices / IIS PowerShell automation
**Confidence:** HIGH (targeted hotfix — all three bugs map to well-documented APIs already in the stack)

## Summary

All three bugs are scoped, low-surface defects fixable inside existing abstractions:
- **BUG-001** adds an opt-in cert-trust knob on `SmtpSettings` wired into MailKit's `SmtpClient.ServerCertificateValidationCallback`.
- **BUG-002** adds a dedicated error code + narrow `COMException.HResult == -2147024891 (0x80070005)` handler inside `ChangePasswordInternal` in `PasswordChangeProvider.cs`.
- **BUG-003** reads the current IIS AppPool identity via `Get-ItemProperty IIS:\AppPools\<pool>` before the provisioning block in `Install-PassReset.ps1` and preserves it when no override is passed.

No shared abstractions change; fixes can land in any order. Phase targets v1.2.3 tag with updated `CHANGELOG.md`, `UPGRADING.md`, and `docs/appsettings-Production.md`.

---

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| BUG-001 | Internal-CA SMTP trust, no silent bypass | MailKit `ServerCertificateValidationCallback` + thumbprint allowlist pattern |
| BUG-002 | `0x80070005` → `PasswordTooRecentlyChanged` with localized message | `COMException.HResult` classification; new `ApiErrorCode` entry + frontend mirror + i18n string |
| BUG-003 | Preserve AppPool identity on upgrade | `Get-ItemProperty IIS:\AppPools\<name>` reads `processModel.identityType` / `processModel.userName` |

---

## BUG-001: SMTP SSL with internal CA

### Root cause
`SmtpEmailService.cs:53` constructs `new SmtpClient()` and calls `ConnectAsync` without setting `ServerCertificateValidationCallback`. MailKit's default callback delegates to `ServicePointManager` / `SslStream` which rejects chains rooted in a CA not in the Windows trust store. No escape hatch exists in `SmtpSettings.cs` today.

### Fix approach (recommended)
Extend `SmtpSettings` with three **mutually-exclusive** opt-in knobs, in order of preference:

1. `TrustedCertificateThumbprints: string[]` — explicit allowlist of SHA-1/SHA-256 thumbprints. The callback returns `true` iff the leaf's thumbprint is in the set, regardless of chain errors.
2. `UseSystemCertificateStore: bool` (default `true`) — relies on OS trust only; operator installs internal-CA root into `LocalMachine\Root`. No code change needed at runtime; documents the preferred path.
3. No `AcceptAllCertificates` flag. Explicitly reject.

Wire the callback in the retry loop just after `new SmtpClient()`:

```csharp
client.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
{
    if (errors == SslPolicyErrors.None) return true;
    if (_settings.TrustedCertificateThumbprints is { Length: > 0 } allow
        && cert is X509Certificate2 c2
        && allow.Any(t => string.Equals(t.Replace(" ",""), c2.Thumbprint,
            StringComparison.OrdinalIgnoreCase)))
    {
        _logger.LogWarning("SMTP cert {Thumb} accepted via thumbprint allowlist ({Errors})",
            c2.Thumbprint, errors);
        return true;
    }
    _logger.LogError("SMTP cert validation failed: {Errors} subject={Subject}",
        errors, cert?.Subject);
    return false;
};
```

### API specifics (MailKit 4.15.1)
- `MailKit.Net.Smtp.SmtpClient.ServerCertificateValidationCallback` is a `RemoteCertificateValidationCallback` — same signature as .NET's `SslStream`.
- Do **not** set `SmtpClient.CheckCertificateRevocation = false` blindly; keep the default `true` unless the operator also documents why CRL/OCSP is unreachable.
- `X509Certificate2.Thumbprint` is SHA-1 hex (uppercase, no spaces) — normalize user input.

### Pitfalls
- **Silent bypass anti-pattern:** `return true;` in the callback. CI must keep that pattern out. Add a code comment "// NEVER return true unconditionally."
- **Thumbprint algo confusion:** SHA-1 thumbprints are deprecated but are what Windows Cert MMC still displays; support SHA-1 and SHA-256 (check length 40 vs 64).
- **CRL offline in air-gapped envs:** When trust is via system store + internal CA with no OCSP, `X509ChainStatusFlags.RevocationStatusUnknown` will fire; thumbprint allowlist dodges this cleanly.
- **Don't log full cert:** log thumbprint + subject only.

### Verification hooks
- Manual: point at a test SMTP (e.g. MailHog behind nginx with a self-signed cert) — connection fails; add the thumbprint → succeeds.
- Unit-testable: extract the callback to a pure function `CertificateTrust.IsTrusted(cert, chain, errors, allowedThumbprints)` and assert against synthetic `X509Certificate2` fixtures. (Phase 2 / QA-001 will add the xUnit scaffold; for v1.2.3 we just structure code to be test-ready.)
- Doc: add `SmtpSettings.TrustedCertificateThumbprints` row to `docs/appsettings-Production.md`.

---

## BUG-002: E_ACCESSDENIED → PasswordTooRecentlyChanged

### Root cause
`ChangePasswordInternal` at `PasswordChangeProvider.cs:361` catches `COMException`, branches on `AllowSetPasswordFallback`, then rethrows. The rethrow hits the top-level `catch (Exception ex)` at line 118 which returns `ApiErrorCode.Generic` → UI shows "Unexpected Error."

The pre-check `CheckMinimumPasswordAge` at line 308 uses `DirectoryEntry.Properties["minPwdAge"]` — but a DC can still reject `ChangePassword()` with `0x80070005` when:
- `lastSet` was slightly stale vs. the DC's current clock
- A non-PDC DC answered the query and a replication lag hid the true `pwdLastSet`
- The DC policy differs from the queried naming context

So the pre-check is not redundant — it catches most cases — but the AD rejection is the last line of defence and must map to a clean error.

### Fix approach
Add one enum value, map the HResult, surface a message:

1. **`ApiErrorCode.cs`** — add `PasswordTooRecentlyChanged = 19` (after `ApproachingLockout = 18`). Keep numeric ordering stable.
2. **Frontend mirror `src/PassReset.Web/ClientApp/src/types/settings.ts`** — add `PasswordTooRecentlyChanged: 19` to the `ApiErrorCode` const at line 111; add `errorPasswordTooRecentlyChanged?: string` to the `Alerts` interface at line 33.
3. **Provider** — inside `ChangePasswordInternal`'s `catch (COMException comEx)`, classify by `comEx.HResult`:

```csharp
const int E_ACCESSDENIED = unchecked((int)0x80070005);
// CONSTRAINT_ATT_TYPE (0x8007202F) sometimes also surfaces for min-age on some DCs
const int ERROR_DS_CONSTRAINT_VIOLATION = unchecked((int)0x8007202F);

if (comEx.HResult == E_ACCESSDENIED || comEx.HResult == ERROR_DS_CONSTRAINT_VIOLATION)
{
    _logger.LogWarning(comEx,
        "AD rejected ChangePassword for {User} with HRESULT=0x{Hex:X8} — treating as minimum-password-age violation",
        userPrincipal.SamAccountName, comEx.HResult);
    throw new ApiErrorException(new ApiErrorItem(
        ApiErrorCode.PasswordTooRecentlyChanged,
        "Your password was changed too recently. Please wait before trying again."));
}
```

The existing `catch (Exception ex)` at line 118 already handles `ApiErrorException` via `apiError.ToApiErrorItem()` so the new mapping propagates cleanly without restructuring.

4. **SIEM** — no new event type needed; log under existing `ValidationFailed` or add a trace-level SIEM entry at the call site in `PasswordController` where `PasswordTooYoung` is already handled (if any) — verify by grep during plan phase.
5. **User-facing copy** — `appsettings.json` → `ClientSettings.Alerts.errorPasswordTooRecentlyChanged`: `"Your password was changed too recently. Please wait and try again."` — matches existing `errorPasswordTooYoung` tone.

### API specifics (System.DirectoryServices / .NET 10)
- `COMException.HResult` is the reliable discriminator; `ExtendedError`/`ExtendedErrorMessage` are on `DirectoryServicesCOMException` specifically — `ChangePassword` throws the base `COMException`, so stick with `HResult`.
- `0x80070005 (E_ACCESSDENIED)` is overloaded: it also fires when the service account lacks "Change Password" rights. **Mitigation:** Our pre-check at line 85 (`userPrincipal.LastPasswordSet`) + group validation + `UserCannotChangePassword` check mean that by the time we reach `ChangePasswordInternal`, access-denied is overwhelmingly a min-age rejection. Log `comEx.Message` + `HResult` so an operator can distinguish misuse from min-age.
- **Do not** swallow the COMException silently — keep the log line; only the surfaced API error changes.

### Pitfalls
- **Over-eager mapping:** If we map *every* access-denied to `PasswordTooRecentlyChanged`, a misconfigured service account will mask as a user problem. Mitigation: the warning log preserves HResult + message; operators diagnose via logs.
- **Interaction with `AllowSetPasswordFallback`:** Must classify the HResult **before** the fallback branch. Current code short-circuits-and-rethrows when `UseAutomaticContext || !AllowSetPasswordFallback`. Move HResult classification to the top of the catch.
- **SetPassword bypasses history:** unchanged — do not route min-age rejection into SetPassword fallback.
- **Pre-check already exists** (`PasswordTooYoung`, `ApiErrorCode = 13`). BUG-002's new code is the *AD-side* rejection only. Keep both error codes: `PasswordTooYoung` = portal pre-check, `PasswordTooRecentlyChanged` = AD rejection. Alternatively, reuse `PasswordTooYoung` and just add the HResult mapping — simpler, one code path. **Recommendation for planner:** reuse `PasswordTooYoung` unless the copy must differ. Acceptance text in REQUIREMENTS.md literally says "PasswordTooRecentlyChanged" so we add the new code.

### Verification hooks
- Manual: change a user's password twice in a row within `minPwdAge`. Expected: UI shows localized "too recently changed" message.
- Unit-testable (future): mock `UserPrincipal.ChangePassword` to throw `COMException` with `HResult = -2147024891`; assert returned `ApiErrorItem.ErrorCode == PasswordTooRecentlyChanged`.

---

## BUG-003: AppPool identity preservation

### Root cause
`Install-PassReset.ps1` lines 303–318 unconditionally sets `processModel.identityType` on **every** run:
- If `-AppPoolIdentity` is passed: sets to `SpecificUser` (3).
- If not passed: sets to `ApplicationPoolIdentity` (4) — **this is the bug**. On upgrade, an admin who previously configured `CORP\svc-passreset` manually (e.g. via IIS Manager after first install) gets silently reset to the built-in virtual account. AD bind then fails on next worker recycle.

### Fix approach
Pre-read existing identity, branch explicitly:

```powershell
# Read current identity BEFORE provisioning block
$existingIdentityType = $null
$existingIdentity     = $null
if ($poolExists) {
    $existingIdentityType = (Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType).Value
    if ($existingIdentityType -eq 'SpecificUser') {
        $existingIdentity = (Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.userName).Value
    }
}

if ($AppPoolIdentity) {
    # Explicit operator override — current behaviour
    if (-not $AppPoolPassword) { Abort '...' }
    # ... existing SpecificUser branch ...
    Write-Ok "App pool identity: $AppPoolIdentity (explicit override)"
}
elseif ($poolExists -and $existingIdentityType -eq 'SpecificUser') {
    # Preserve existing service account — DO NOT touch password or identityType
    Write-Ok "App pool identity preserved: $existingIdentity (use -AppPoolIdentity to override)"
}
elseif ($poolExists) {
    # Existing pool using ApplicationPoolIdentity / NetworkService / LocalSystem — leave untouched on upgrade
    Write-Ok "App pool identity preserved: $existingIdentityType"
}
else {
    # Fresh install, no override → default to ApplicationPoolIdentity
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 4
    Write-Ok 'App pool identity: ApplicationPoolIdentity (new pool default)'
}
```

### API specifics (WebAdministration module)
- `Get-ItemProperty IIS:\AppPools\<name> -Name processModel.identityType` returns a string: `LocalSystem`, `LocalService`, `NetworkService`, `SpecificUser`, `ApplicationPoolIdentity`. **Note:** despite the internal numeric mapping (0–4) used by `Set-ItemProperty`, the getter returns the enum name.
- `processModel.userName` is populated only when `identityType = SpecificUser`.
- No need to read `processModel.password` — it's write-only (returns empty string); preserving identity means not touching the password either.
- NTFS ACL block at line 384 already branches on `$AppPoolIdentity` presence — extend it to also use `$existingIdentity` when preserving:

```powershell
$identity = if ($AppPoolIdentity) { $AppPoolIdentity }
            elseif ($existingIdentityType -eq 'SpecificUser') { $existingIdentity }
            else { "IIS AppPool\$AppPoolName" }
```

### Pitfalls
- **Don't clobber the password:** Even reading + re-writing `processModel.password` would fail — IIS stores it encrypted and does not round-trip. Solution: never call `Set-ItemProperty processModel.password` in the preserve branch.
- **Fresh install semantics unchanged:** Pool doesn't exist → default to `ApplicationPoolIdentity`. Only the *upgrade* path changes.
- **NTFS ACL must match actual identity:** Current code assumes `$AppPoolIdentity` OR built-in; the preserve branch must feed `$existingIdentity` into the ACL block at line 384 or the service account loses Read/Execute on reinstall.
- **Implicit `-Force` unattended path:** If operator passes `-AppPoolIdentity ""` explicitly (empty string from CI), PowerShell treats that as "no identity" — current behaviour. Keep the `if ($AppPoolIdentity)` test on non-empty string, same as today.
- **Script-scope `Set-StrictMode -Version Latest`:** referencing `$existingIdentity` when unset triggers error. Initialize to `$null` at top of the block (shown above).

### Verification hooks
- Manual repro: fresh install without `-AppPoolIdentity`. Manually set pool to `CORP\svc-passreset` via IIS Manager. Re-run installer without `-AppPoolIdentity`. Expected: `Get-ItemProperty IIS:\AppPools\PassResetPool -Name processModel.userName` still returns `CORP\svc-passreset`.
- Manual override: re-run with `-AppPoolIdentity CORP\svc-other -AppPoolPassword ...`. Expected: pool switches to the new identity.
- Unattended CI path (`-Force`, no `-AppPoolIdentity`): identity preserved, no prompts.
- Doc: add an "AppPool identity on upgrade" section to `UPGRADING.md`.

---

## Cross-cutting notes

### Commit order (no hard dependencies — parallelizable)
Recommended order for clean review + CHANGELOG narrative:

1. `fix(provider): map E_ACCESSDENIED from AD to PasswordTooRecentlyChanged` — includes `PassReset.Common/ApiErrorCode.cs`, `PasswordChangeProvider.cs`, `types/settings.ts`, `appsettings.json` alert string. Touches two projects; land first to get the enum + i18n shape merged early.
2. `fix(web): support internal-CA SMTP trust via thumbprint allowlist` — isolated to `SmtpSettings.cs`, `SmtpEmailService.cs`, `docs/appsettings-Production.md`.
3. `fix(installer): preserve existing IIS AppPool identity on upgrade` — isolated to `deploy/Install-PassReset.ps1`, `UPGRADING.md`.

All three scopes match the allowed commit-scope list (`provider`, `web`/`security`, `installer`) per `CLAUDE.md`.

### Shared test infrastructure
**None** for v1.2.3 — QA-001 (xUnit + Vitest) is a Phase 2 deliverable. Keep code test-ready (extract the SMTP callback to a pure function; keep HResult constants named) but do not introduce a test project yet. Verification for this phase is manual + existing CI (tsc + Release build + ESLint).

### Release / CHANGELOG updates
Required before tag:
- `CHANGELOG.md` — promote `[Unreleased]` → `[1.2.3] — YYYY-MM-DD` with three bullets under `### Fixed`.
- `UPGRADING.md` — new section: "Upgrading from v1.2.2 → v1.2.3" with AppPool-identity preservation note and new optional `TrustedCertificateThumbprints` SMTP setting.
- `docs/appsettings-Production.md` — new `SmtpSettings.TrustedCertificateThumbprints` row; new `ClientSettings.Alerts.errorPasswordTooRecentlyChanged` row.
- `README.md` — no user-visible UI change; skip unless CHANGELOG is linked.
- Tag `v1.2.3` → `release.yml` triggers automatically.

### Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `0x80070005` during `UserPrincipal.ChangePassword` is overwhelmingly min-pwd-age rejection on well-configured service accounts | BUG-002 | If service account lacks rights, users see a misleading "too recent" message — mitigated by preserving HResult in warning logs |
| A2 | MailKit 4.15.1's `ServerCertificateValidationCallback` signature matches stock `SslStream` callback | BUG-001 | If API changed in 4.x, signature adjustment needed — verify by Context7/docs during plan phase |
| A3 | `Get-ItemProperty IIS:\AppPools\<n> -Name processModel.identityType` returns enum name (`SpecificUser`) not numeric (3) on Windows Server 2019/2022/2025 | BUG-003 | Comparison would fail silently; verify on a test VM during plan phase, or compare against both forms defensively |
| A4 | Adding `ApiErrorCode = 19` is non-breaking across config upgrades | BUG-002 | Enum values are additive and backward-compatible by design; low risk |

### Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| MailKit | BUG-001 | ✓ (NuGet) | 4.15.1 | — |
| System.DirectoryServices.AccountManagement | BUG-002 | ✓ (net10.0-windows) | 10.0.5 | — |
| WebAdministration PS module | BUG-003 | ✓ (IIS role) | built-in | — |
| Test AD + test SMTP | Manual verification | ✗ (likely) | — | Defer verification to staging environment |

### Project Constraints (from CLAUDE.md)

- **GSD workflow:** edits must flow through a GSD command — this hotfix runs under `/gsd:execute-phase`.
- **Commit format:** `type(scope): subject`; scopes `provider`, `web`, `installer`, `docs`, `security`.
- **No automated tests yet:** verification is `dotnet build -c Release` + `npm run lint` + `tsc` + manual repro.
- **Windows-only:** fixes target `net10.0-windows`; PowerShell fix is Windows Server 2019+.
- **No breaking config changes:** new `SmtpSettings.TrustedCertificateThumbprints` must default to empty array/null → no-op; existing appsettings continue to work.
- **Secrets:** no new secrets; thumbprints are not secret.

---

## Sources

### Primary (HIGH confidence)
- Project files read in this session: `SmtpEmailService.cs`, `PasswordChangeProvider.cs`, `ApiErrorCode.cs`, `settings.ts`, `Install-PassReset.ps1`, `SmtpSettings.cs`, `PROJECT.md`, `REQUIREMENTS.md`, `ROADMAP.md`, `Todo.MD`, `CLAUDE.md`.
- MailKit FAQ referenced in Todo.MD: https://github.com/jstedfast/MailKit/blob/master/FAQ.md#ssl-handshake-exception

### Secondary (MEDIUM confidence — verify during plan phase if needed)
- .NET `COMException.HResult` semantics for AD password operations — well-known `E_ACCESSDENIED = 0x80070005`, `ERROR_DS_CONSTRAINT_VIOLATION = 0x8007202F`.
- IIS `processModel.identityType` enum values — standard across Server 2016+.

### Tertiary / `[ASSUMED]`
- See Assumptions Log A1–A4.

---

**Research date:** 2026-04-14
**Valid until:** 2026-05-14 (stable stack; hotfix scope narrow)
