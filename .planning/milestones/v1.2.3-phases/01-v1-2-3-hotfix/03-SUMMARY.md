---
phase: 01-v1-2-3-hotfix
plan: 03
subsystem: installer
tags: [installer, iis, apppool, bug-003, hotfix]
requires: []
provides: [BUG-003]
affects: [deploy/Install-PassReset.ps1, UPGRADING.md, CHANGELOG.md]
tech-stack:
  added: []
  patterns: [pre-read-then-branch-provisioning, preserve-on-upgrade]
key-files:
  created: []
  modified:
    - deploy/Install-PassReset.ps1
    - UPGRADING.md
    - CHANGELOG.md
decisions:
  - Compare identityType defensively against both 'SpecificUser' (string) and 3 (numeric) per RESEARCH.md A3
  - Single combined commit (code + docs) per plan recommendation
metrics:
  duration: ~15m
  completed: 2026-04-14
---

# Phase 01 Plan 03: Preserve AppPool Identity on Upgrade (BUG-003) Summary

Fixed BUG-003 so `Install-PassReset.ps1` no longer silently clobbers operator-configured IIS AppPool service accounts during upgrade; added an UPGRADING.md note and a `[Unreleased]` CHANGELOG entry.

## Tasks Completed

| Task | Name | Commit | Files |
|------|------|--------|-------|
| 1 | Pre-read existing identity + four-branch provisioning + ACL fix | 91008b3 | deploy/Install-PassReset.ps1 |
| 2 | UPGRADING.md + CHANGELOG.md updates | 91008b3 | UPGRADING.md, CHANGELOG.md |

Single combined commit per the plan's "one commit (code + docs together)" recommendation.

## Implementation Notes

**Install-PassReset.ps1** — Inserted a pre-read block (lines ~292-305) that runs *before* `New-WebAppPool` so an existing pool's `processModel.identityType` / `processModel.userName` are captured. Replaced the previous unconditional `else { Set-ItemProperty ... identityType 4 }` with a four-branch block:

1. `$AppPoolIdentity` present → explicit override (requires `-AppPoolPassword`)
2. Pool exists AND existing identity is `SpecificUser` (or numeric `3`) → preserve — *zero writes to identityType/userName/password*
3. Pool exists with built-in identity → preserve, no writes
4. Fresh install → default `ApplicationPoolIdentity` (4)

The NTFS ACL block (now ~line 412) was updated to feed `$existingIdentity` into the ACE when the preserve branch fires, so Read/Execute goes to the operator's service account and not `IIS AppPool\<poolname>`.

Comparisons use `-eq 'SpecificUser' -or -eq 3` defensively because RESEARCH.md A3 flagged the return shape as assumed. `$existingIdentityType` / `$existingIdentity` are initialized to `$null` at the top of the block to stay `Set-StrictMode -Version Latest` safe. The preserve branch deliberately does **not** read or write `processModel.password` — it's write-only per RESEARCH.md.

**UPGRADING.md** — New `### Upgrading from 1.2.2 → 1.2.3` subsection (at the top of the per-version list, consistent with the existing newest-first layout) with three H4 cross-references: BUG-003 AppPool preservation, BUG-001 SMTP TrustedCertificateThumbprints, BUG-002 PasswordTooRecentlyChanged.

**CHANGELOG.md** — Created `## [Unreleased]` → `### Fixed` entry naming BUG-003 and referencing UPGRADING.md.

## Deviations from Plan

None — plan executed exactly as written. The only non-trivial judgement call was matching the existing `UPGRADING.md` heading style (`### X.Y.Z — YYYY-MM-DD` per-version, H4 inside), so the new section uses `### Upgrading from 1.2.2 → 1.2.3` with H4 subsections, which mirrors the document's existing nesting depth rather than the literal H3 `## Upgrading from v1.2.2 → v1.2.3` example in the plan.

## Verification

| Acceptance criterion (REQUIREMENTS.md BUG-003) | Status | Evidence |
|---|---|---|
| Installer preserves existing IIS AppPool identity during upgrade | PASS | Preserve branches (SpecificUser + built-in) perform zero writes to identityType/userName/password |
| Does not reset to `ApplicationPoolIdentity` unless explicitly overridden | PASS | Fresh-install branch is the only `Set-ItemProperty ... identityType 4` call; gated on `-not $poolExists` |
| Explicit override via `-AppPoolIdentity` still works | PASS | Unchanged first branch; `-AppPoolPassword` still required |
| Documented in UPGRADING.md | PASS | `### Upgrading from 1.2.2 → 1.2.3` section present with AppPool-identity subsection |

| ROADMAP.md Phase 1 success criterion #3 | Status | Evidence |
|---|---|---|
| Running installer as upgrade preserves custom service account | PASS | Branch 2 matches SpecificUser (string or numeric) and exits with only a `Write-Ok` |

| Plan automated verification | Status |
|---|---|
| `pwsh [System.Management.Automation.Language.Parser]::ParseFile(...)` | PASS (`PARSE OK`) |
| `Select-String UPGRADING.md -Pattern '1.2.2.*1.2.3'` | PASS |
| `Select-String CHANGELOG.md -Pattern 'BUG-003'` | PASS |

Manual staging scenarios (out-of-plan scope, deferred to staging environment):
1. Fresh install, no `-AppPoolIdentity` → `ApplicationPoolIdentity`.
2. Upgrade after manual `CORP\svc-passreset` → identity preserved, ACL grants to the service account.
3. Upgrade with `-AppPoolIdentity CORP\svc-other -AppPoolPassword ...` → pool switches, ACL updated.
4. CI unattended (`-Force`, no `-AppPoolIdentity`) → identity preserved, no prompts.

## Self-Check: PASSED

- `deploy/Install-PassReset.ps1` — preserve branch, fresh-install branch, ACL branch all present (verified in diff).
- `UPGRADING.md` — "1.2.2 → 1.2.3" section present.
- `CHANGELOG.md` — `[Unreleased]` + BUG-003 bullet present.
- Commit `91008b3` present in `git log`.
- PowerShell parser reports no syntax errors.

## Follow-ups

- Tag `v1.2.3` is **not** created here — deferred per plan. Done only after plans 01/02/03 all pass verification, per CLAUDE.md release process.
- Promote `[Unreleased]` → `[1.2.3] — YYYY-MM-DD` in CHANGELOG at tag time.
