---
phase: 07-installer-deployment-fixes
reviewed: 2026-04-16T00:00:00Z
depth: standard
files_reviewed: 7
files_reviewed_list:
  - CHANGELOG.md
  - deploy/Install-PassReset.ps1
  - deploy/Uninstall-PassReset.ps1
  - docs/IIS-Setup.md
  - src/PassReset.PasswordProvider/PassReset.PasswordProvider.csproj
  - src/PassReset.PasswordProvider/PasswordChangeProvider.cs
  - src/PassReset.Tests/PasswordProvider/PreCheckMinPwdAgeTests.cs
findings:
  critical: 0
  warning: 3
  info: 4
  total: 7
status: issues_found
---

# Phase 07: Code Review Report

**Reviewed:** 2026-04-16
**Depth:** standard
**Files Reviewed:** 7
**Status:** issues_found

## Summary

Phase 07 ships six STAB-NNN stabilization fixes touching `Install-PassReset.ps1`, `Uninstall-PassReset.ps1`, `PasswordChangeProvider.cs`, docs, and a new test file. The implementation is generally solid — flags are initialized outside strict-mode gates, the `COMException` defense-in-depth floor is preserved, the new `EvaluateMinPwdAge` helper is pure and well-covered by unit tests, and the uninstaller ASCII-only conversion is consistent.

Three warnings concern correctness edge cases: (1) STAB-001's "stop conflicting site(s)" branch never restores the stopped sites if the install later aborts, (2) the fresh-install "reachable URL" message uses `$HttpPort` parameter instead of `$selectedHttpPort` in the upgrade branch, and (3) `PreCheckMinPwdAge` calls `UserPrincipal.FindByIdentity(ctx, username)` without specifying an IdentityType, which can mis-resolve users when the input is not the default UPN form. Info items cover minor code-quality improvements.

Out of scope (as noted): the deliberate non-listing of `Web-Asp-Net45`/`Web-Net-Ext45` in the required IIS features is documented and intentional — not flagged.

## Warnings

### WR-01: STAB-001 does not restore stopped conflicting sites on later abort

**File:** `deploy/Install-PassReset.ps1:448-457`
**Issue:** When the operator chooses option `[1]` "Stop the conflicting site(s) and bind PassReset to port 80", the installer calls `Stop-Website -Name $s` for each foreign site and then proceeds. If a subsequent step aborts (certificate missing, NTFS ACL failure, robocopy ≥ 8, startup failure without backup), the foreign sites are left stopped — a silent availability impact on an unrelated workload. The rollback `catch` block at `:695-721` restarts only `$SiteName`/`$AppPoolName`, not the sites stopped during conflict resolution.
**Fix:** Track the stopped sites in a list and, in the outer `catch` / `finally` (or a `trap`), attempt to restart them. Minimal patch:

```powershell
$stoppedForeignSites = @()
# in switch '1':
foreach ($s in $conflictSites) {
    if ($PSCmdlet.ShouldProcess("IIS site $s", 'Stop')) {
        Stop-Website -Name $s -ErrorAction Stop
        $stoppedForeignSites += $s
        Write-Ok "Stopped site '$s'"
    }
}
# in the rollback catch (line ~720):
foreach ($s in $stoppedForeignSites) {
    try { Start-Website -Name $s -ErrorAction Stop; Write-Ok "Restarted foreign site '$s'" }
    catch { Write-Warn "Could not restart '$s' — restart manually" }
}
```

### WR-02: Upgrade-path "reachable URL" prints `$HttpPort` instead of the real binding

**File:** `deploy/Install-PassReset.ps1:549-553`
**Issue:** The fresh-install branch correctly uses `$selectedHttpPort` for the URL announcement, but the upgrade branch hard-codes `$HttpPort` (the parameter, default 80). If a prior installation is running on an alternate port (e.g. 8081 because port 80 was in use at first-install time), this message now lies — it tells the operator `http://host:80/` when the actual binding is `:8081`. It also does not consult the current HTTP binding(s).
**Fix:** Read the actual bindings from IIS and print each one:

```powershell
$httpBindings = Get-WebBinding -Name $SiteName -Protocol http -ErrorAction SilentlyContinue
foreach ($b in $httpBindings) {
    # $b.bindingInformation is "*:port:host"
    $port = ($b.bindingInformation -split ':')[1]
    Write-Ok "PassReset reachable at http://${hostHeader}:${port}/"
}
```

### WR-03: `PreCheckMinPwdAge` calls `FindByIdentity` without an `IdentityType`

**File:** `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:669`
**Issue:** The pre-check re-resolves the user via `UserPrincipal.FindByIdentity(ctx, username)` (no `IdentityType` arg). The main `PerformPasswordChangeAsync` path uses `FindUser` which tries each attribute in `AllowedUsernameAttributes` (samaccountname → userprincipalname → mail) and handles `DOMAIN\user`, bare sam, and `user@domain` inputs. The overload without an `IdentityType` auto-detects only a limited set (SAM, UPN, SID, GUID, DN) and can return `null` for inputs the main flow already resolved — e.g. when the caller passes an email (configured via `AllowedUsernameAttributes: ["mail"]`) or a `DOMAIN\user` form. The catch path returns `null` silently, which falls through to `ChangePasswordInternal` and the post-hoc `COMException` catch — so the fast-path is defeated for exactly those users.
**Fix:** Reuse the existing private `FindUser` helper so the pre-check matches the real flow's resolution:

```csharp
using var user = FindUser(ctx, username);
if (user == null) return null;
```

This also avoids a second full AD lookup per request on the hot path (the caller already has a resolved `UserPrincipal`). Consider passing the already-resolved `UserPrincipal` into `PreCheckMinPwdAge(userPrincipal)` instead of re-resolving.

## Info

### IN-01: Comment claims "Re-saved as UTF-8 with BOM" for Uninstall-PassReset.ps1 — verify encoding

**File:** `deploy/Uninstall-PassReset.ps1` (whole file)
**Issue:** CHANGELOG entry STAB-005 states the uninstaller was re-saved as UTF-8-with-BOM. The file contents now use only ASCII glyphs (`---` dividers, `[>>]`, `[OK]`, `[!!]`, `[ERR]`) — correct — but the reviewer cannot verify byte-level BOM presence from the textual read. Operators on Windows PowerShell 5.1 will reject a BOM-less UTF-8 file silently in some locales.
**Fix:** Add a CI check (or a one-liner in the release script) that asserts the first three bytes of `deploy/*.ps1` equal `0xEF 0xBB 0xBF`:

```powershell
$bytes = [IO.File]::ReadAllBytes('deploy/Uninstall-PassReset.ps1')[0..2]
if ($bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) { throw 'BOM missing' }
```

### IN-02: `EvaluateMinPwdAge` `lastSet.ToUniversalTime()` on already-UTC input is a no-op but asymmetric to `now`

**File:** `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:700`
**Issue:** `elapsed = now - lastSet.ToUniversalTime()` normalizes `lastSet` but not `now`. The test fixture passes `now` with `DateTimeKind.Utc`, so this is fine in tests, but if a future caller passes a `Local` or `Unspecified` `DateTime` for `now` (the production call site uses `DateTime.UtcNow`, which is safe), `elapsed` is wrong. Defense-in-depth would be to normalize both.
**Fix:**

```csharp
var elapsed = now.ToUniversalTime() - lastSet.ToUniversalTime();
```

### IN-03: `$existingBinding` variable shadows outer-scope semantics

**File:** `deploy/Install-PassReset.ps1:515-517`
**Issue:** Minor readability: the HTTPS `$existingBinding` and HTTP `$httpBinding` are logically parallel but named differently. Not a bug — just inconsistent. Consider renaming for symmetry.

### IN-04: STAB-004 `_logger.LogWarning` on pre-check hit uses only minute-level granularity

**File:** `src/PassReset.PasswordProvider/PasswordChangeProvider.cs:678-681`
**Issue:** The diagnostic log event includes `lastSet=...` and `minAge=...` but not the computed `remainingMinutes`. Operators correlating SIEM events to the user-visible error message have to recompute the delta. Low priority.
**Fix:** Add the computed remaining minutes:

```csharp
_logger.LogWarning(
    "STAB-004 pre-check blocked consecutive change for {User}: lastSet={LastSet} minAge={MinAge} remainingMinutes={Remaining}",
    username, lastSet.Value.ToUniversalTime(), minAge,
    (int)Math.Ceiling((minAge - (DateTime.UtcNow - lastSet.Value.ToUniversalTime())).TotalMinutes));
```

---

_Reviewed: 2026-04-16_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
