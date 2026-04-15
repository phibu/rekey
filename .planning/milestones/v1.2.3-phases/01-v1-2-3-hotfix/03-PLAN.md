---
phase: 01-v1-2-3-hotfix
plan: 03
type: execute
wave: 1
depends_on: []
files_modified:
  - deploy/Install-PassReset.ps1
  - UPGRADING.md
  - CHANGELOG.md
autonomous: true
requirements:
  - BUG-003

must_haves:
  truths:
    - "On upgrade, when an existing AppPool has identityType=SpecificUser, Install-PassReset.ps1 preserves both identityType and userName (does NOT touch processModel.password)"
    - "On upgrade, when an existing AppPool has a built-in identity (ApplicationPoolIdentity/NetworkService/LocalService/LocalSystem), the installer leaves it untouched"
    - "On fresh install with no -AppPoolIdentity parameter, the installer defaults to ApplicationPoolIdentity (existing behaviour)"
    - "When -AppPoolIdentity is passed explicitly, the installer overrides existing identity (explicit override wins, requires -AppPoolPassword for SpecificUser)"
    - "NTFS ACL granting Read/Execute uses the *actual* identity (explicit override OR preserved existing) — never a stale computed default"
  artifacts:
    - path: "deploy/Install-PassReset.ps1"
      provides: "Pre-read of existing processModel.identityType/userName + branched provisioning block that preserves on upgrade"
      contains: "existingIdentityType"
    - path: "UPGRADING.md"
      provides: "New section: 'Upgrading from v1.2.2 → v1.2.3' documenting AppPool-identity preservation + new SMTP TrustedCertificateThumbprints note"
      contains: "AppPool identity"
  key_links:
    - from: "deploy/Install-PassReset.ps1 (provisioning block, ~line 303)"
      to: "deploy/Install-PassReset.ps1 (NTFS ACL block, ~line 384)"
      via: "$existingIdentity variable used in ACL identity resolution"
      pattern: "existingIdentity"
---

<objective>
Fix BUG-003: `Install-PassReset.ps1` must preserve the IIS AppPool identity of an existing installation during upgrade. Today the installer unconditionally resets `processModel.identityType` to `ApplicationPoolIdentity` when `-AppPoolIdentity` is not passed — clobbering operator-configured service accounts (e.g. `CORP\svc-passreset`) and breaking AD bind on the next worker recycle.

Purpose: Ship a safe upgrade path from v1.2.2 to v1.2.3 that does not regress manually-configured AppPool identities, without changing fresh-install semantics or the explicit-override code path.

Output: Branched identity-provisioning logic in `Install-PassReset.ps1`, consistent NTFS ACL identity resolution, `UPGRADING.md` section, CHANGELOG entry.
</objective>

<execution_context>
@C:/Users/Phibu/Claude-Projekte/AD-Passreset-Portal/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Phibu/Claude-Projekte/AD-Passreset-Portal/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/REQUIREMENTS.md
@.planning/phases/01-v1-2-3-hotfix/RESEARCH.md

@deploy/Install-PassReset.ps1
@UPGRADING.md

<interfaces>
<!-- WebAdministration PS module (built-in on IIS-enabled Windows Server) -->

Get-ItemProperty IIS:\AppPools\<name> -Name processModel.identityType
  => Returns a string: one of
     'LocalSystem' | 'LocalService' | 'NetworkService' | 'SpecificUser' | 'ApplicationPoolIdentity'
  (Research assumption A3 — verify during impl by actually running this on a test pool
   and comparing; if it comes back numeric on some OS versions, compare defensively against both.)

Get-ItemProperty IIS:\AppPools\<name> -Name processModel.userName
  => Non-empty only when identityType == 'SpecificUser'

Set-ItemProperty IIS:\AppPools\<name> processModel.password
  => write-only; NEVER read or round-trip (returns empty string).
  NEVER call this in the preserve branch.
</interfaces>
</context>

<tasks>

<task type="auto">
  <name>Task 1: Pre-read existing identity and branch provisioning logic</name>
  <files>deploy/Install-PassReset.ps1</files>
  <action>
    In `deploy/Install-PassReset.ps1`, **before** the current identity-provisioning block (around line 303 per RESEARCH.md, just after `$poolExists` is determined), capture the existing identity:

    ```powershell
    # BUG-003: Capture existing AppPool identity BEFORE any provisioning so we can preserve it on upgrade.
    $existingIdentityType = $null
    $existingIdentity     = $null
    if ($poolExists) {
        try {
            $existingIdentityType = (Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.identityType -ErrorAction Stop).Value
            if ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3) {
                $existingIdentity = (Get-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.userName -ErrorAction Stop).Value
            }
        } catch {
            Write-Warning "Could not read existing AppPool identity: $($_.Exception.Message). Will fall through to default handling."
        }
    }
    ```

    Defensive: compare against both string (`'SpecificUser'`) and numeric (`3`) because RESEARCH.md A3 marks the return shape as assumed — running on some Server versions may return the integer. Same pattern applies to the other identity-type branches.

    Replace the unconditional `Set-ItemProperty ... processModel.identityType 4` block with an explicit branch:

    ```powershell
    if ($AppPoolIdentity) {
        # Explicit operator override — current behaviour preserved.
        if (-not $AppPoolPassword) { Abort 'Pass -AppPoolPassword when using -AppPoolIdentity.' }
        Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 3
        Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.userName $AppPoolIdentity
        Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.password $AppPoolPassword
        Write-Ok "App pool identity: $AppPoolIdentity (explicit override)"
    }
    elseif ($poolExists -and ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3)) {
        # Preserve existing service account: DO NOT touch identityType, userName, or password.
        Write-Ok "App pool identity preserved: $existingIdentity (use -AppPoolIdentity to override)"
    }
    elseif ($poolExists) {
        # Existing built-in identity (ApplicationPoolIdentity / NetworkService / LocalService / LocalSystem) — leave untouched on upgrade.
        Write-Ok "App pool identity preserved: $existingIdentityType"
    }
    else {
        # Fresh install, no override → default to ApplicationPoolIdentity (4).
        Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 4
        Write-Ok 'App pool identity: ApplicationPoolIdentity (new pool default)'
    }
    ```

    **Do NOT** attempt to read or re-write `processModel.password` — it's write-only and does not round-trip. The preserve branch deliberately touches nothing.

    **Also update the NTFS ACL block at ~line 384** to feed the *actual* runtime identity into the ACE:

    ```powershell
    $identity = if ($AppPoolIdentity) {
        $AppPoolIdentity
    } elseif ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3) {
        $existingIdentity
    } else {
        "IIS AppPool\$AppPoolName"
    }
    ```

    The existing ACL grant (Read/Execute on the install path) should use this `$identity` variable uniformly. Without this change, the preserve branch grants ACL to the wrong principal and the real service account silently loses file access.

    Preserve strict-mode compatibility: `$existingIdentityType` and `$existingIdentity` are initialized to `$null` at the top of the block so `Set-StrictMode -Version Latest` does not fault on unset references.

    Per RESEARCH.md pitfalls:
    - Empty string `-AppPoolIdentity ''` from CI must continue to be treated as "no override" — the `if ($AppPoolIdentity)` test remains truthy only for non-empty strings.
    - Fresh-install semantics unchanged.
  </action>
  <verify>
    <automated>pwsh -NoProfile -Command "Invoke-ScriptAnalyzer -Path deploy/Install-PassReset.ps1 -Severity Error"</automated>
  </verify>
  <done>
    Script parses without syntax errors. No PSScriptAnalyzer Error-severity hits introduced. Four-branch identity logic is in place (explicit override / preserve SpecificUser / preserve built-in / fresh-install default). NTFS ACL block resolves `$identity` consistently with the preserve branch. No reads or writes of `processModel.password` in the preserve path.
  </done>
</task>

<task type="auto">
  <name>Task 2: Update UPGRADING.md and CHANGELOG, reference both hotfix knobs</name>
  <files>UPGRADING.md, CHANGELOG.md</files>
  <action>
    1. `UPGRADING.md` — add a new top-level section ABOVE existing per-version sections (or inserted in chronological order consistent with current file structure):

       ```
       ## Upgrading from v1.2.2 → v1.2.3

       ### AppPool identity is now preserved on upgrade (BUG-003)

       `Install-PassReset.ps1` no longer resets the IIS AppPool identity when
       `-AppPoolIdentity` is not passed. If you previously configured a custom
       service account (for example `CORP\svc-passreset`) via IIS Manager or a
       prior install, the upgrade leaves it in place.

       - Fresh installs still default to `ApplicationPoolIdentity`.
       - Pass `-AppPoolIdentity CORP\<user> -AppPoolPassword <pw>` to override.
       - Built-in identities (`ApplicationPoolIdentity`, `NetworkService`,
         `LocalService`, `LocalSystem`) are also preserved on upgrade.

       Verify after upgrade:
       ```
       Get-ItemProperty IIS:\AppPools\PassResetPool -Name processModel.userName
       ```
       The value should match what you had configured pre-upgrade.

       ### Optional: internal-CA SMTP trust (BUG-001)

       If your SMTP relay uses a certificate issued by an internal CA that is not
       in `LocalMachine\Root`, you can now add explicit thumbprints to
       `SmtpSettings.TrustedCertificateThumbprints` (SHA-1 or SHA-256 hex). See
       `docs/appsettings-Production.md`. No change required for deployments
       using public CAs or already-trusted internal CAs.

       ### Clearer error on minimum-password-age rejection (BUG-002)

       Users who retry a password change within the domain's `minPwdAge` now see
       a dedicated localized message (`errorPasswordTooRecentlyChanged`) instead
       of "Unexpected Error." Override the copy via
       `ClientSettings.Alerts.errorPasswordTooRecentlyChanged` if desired.
       ```

       (If `UPGRADING.md` uses a different heading style / ordering, mirror that style — keep the three subsections.)

    2. `CHANGELOG.md` — under `[Unreleased]` → `### Fixed`, add:
       ```
       - **Installer**: `Install-PassReset.ps1` now preserves the existing IIS
         AppPool identity on upgrade. Previously, running the installer without
         `-AppPoolIdentity` would reset a manually-configured service account
         back to `ApplicationPoolIdentity`. Fresh-install default and explicit
         `-AppPoolIdentity` override behaviour are unchanged. See `UPGRADING.md`.
         (BUG-003)
       ```
  </action>
  <verify>
    <automated>pwsh -NoProfile -Command "if (-not (Test-Path UPGRADING.md)) { exit 1 }; if (-not (Select-String -Path UPGRADING.md -Pattern 'v1.2.2.*v1.2.3' -Quiet)) { exit 1 }; if (-not (Select-String -Path CHANGELOG.md -Pattern 'BUG-003' -Quiet)) { exit 1 }; exit 0"</automated>
  </verify>
  <done>
    UPGRADING.md has a v1.2.2 → v1.2.3 section mentioning AppPool-identity preservation. CHANGELOG.md `[Unreleased]` lists the installer fix. Cross-references to BUG-001 and BUG-002 appear in UPGRADING.md for discoverability.
  </done>
</task>

</tasks>

<verification>
- PowerShell syntax: `pwsh -NoProfile -Command "[System.Management.Automation.Language.Parser]::ParseFile('deploy/Install-PassReset.ps1', [ref]$null, [ref]$null)"` — no parse errors.
- Script analyzer: `Invoke-ScriptAnalyzer -Path deploy/Install-PassReset.ps1 -Severity Error` — zero Error-severity findings introduced by this change.
- `git grep -n "existingIdentityType\|existingIdentity " deploy/Install-PassReset.ps1` shows both pre-read at top and consumption in the provisioning branch + ACL branch.
- `git grep -n "processModel.password" deploy/Install-PassReset.ps1` — password is only written in the explicit-override branch; the preserve branch contains no such line.
- Manual scenarios (staging, out of plan scope):
  1. Fresh install, no `-AppPoolIdentity` → pool uses `ApplicationPoolIdentity`.
  2. Upgrade after manually setting pool to `CORP\svc-passreset` via IIS Manager, no `-AppPoolIdentity` → `processModel.userName` still `CORP\svc-passreset`; no password prompt; NTFS ACL on install path grants Read/Execute to `CORP\svc-passreset`.
  3. Upgrade with explicit `-AppPoolIdentity CORP\svc-other -AppPoolPassword ...` → pool switches to new identity; ACL updated to new principal.
  4. Unattended CI (`-Force`, no `-AppPoolIdentity`) → identity preserved, no prompts.
</verification>

<acceptance_criteria>
From REQUIREMENTS.md BUG-003:
> `deploy/Install-PassReset.ps1` preserves the existing IIS AppPool identity during upgrade (does not reset to `ApplicationPoolIdentity`) unless explicitly overridden via parameter. Documented in `UPGRADING.md`.

From ROADMAP.md Phase 1 success criterion #3:
> Running `Install-PassReset.ps1` as an upgrade preserves the existing IIS AppPool identity (custom service account is not reset to `ApplicationPoolIdentity`).
</acceptance_criteria>

<pitfalls>
From RESEARCH.md BUG-003:
- **Don't clobber the password:** Never read/write `processModel.password` in the preserve branch — it's write-only and doesn't round-trip.
- **Fresh install semantics unchanged:** Pool doesn't exist → default to `ApplicationPoolIdentity`. Only the upgrade path changes.
- **NTFS ACL must match actual identity:** The ACL block at ~line 384 must feed from `$existingIdentity` when preserving, or the service account silently loses Read/Execute.
- **Implicit `-Force` unattended path:** Empty-string `-AppPoolIdentity ''` from CI must still behave as "no override."
- **Strict mode:** Initialize `$existingIdentityType`/`$existingIdentity` to `$null` at block top — `Set-StrictMode -Version Latest` faults on unset references.
- **Return-shape assumption A3:** `processModel.identityType` getter is documented to return the enum name, but verify on the actual OS during first real run; compare defensively against both `'SpecificUser'` and `3`.
</pitfalls>

<success_criteria>
- BUG-003 ready for release verification.
- Fresh-install path and explicit-override path are byte-equivalent in behaviour to v1.2.2.
- Upgrade path preserves all four existing identity shapes (ApplicationPoolIdentity, NetworkService, LocalService, LocalSystem, SpecificUser).
- UPGRADING.md v1.2.2 → v1.2.3 section exists and links/references all three hotfix REQs for operator discoverability.
</success_criteria>

<commits>
Expected: one commit (code + docs together per CLAUDE.md commit-workflow step 1-2 which requires README/docs checked before commit).
- `fix(installer): preserve existing IIS AppPool identity on upgrade (BUG-003)` — Install-PassReset.ps1 + UPGRADING.md + CHANGELOG.md.

Two-commit alternative:
- `fix(installer): preserve existing IIS AppPool identity on upgrade (BUG-003)` — Install-PassReset.ps1
- `docs(deploy): document AppPool-identity preservation in UPGRADING.md` — UPGRADING.md + CHANGELOG.md
</commits>

<output>
After completion, create `.planning/phases/01-v1-2-3-hotfix/03-SUMMARY.md` per template.

**After-phase step (not in this plan):** Tag `v1.2.3` happens only after plans 01, 02, 03 all pass verification. Per CLAUDE.md release process: merge to master → promote CHANGELOG `[Unreleased]` → `[1.2.3] — YYYY-MM-DD` → `git tag -a v1.2.3 -m "Release v1.2.3" && git push origin v1.2.3` → `release.yml` builds and publishes.
</output>
