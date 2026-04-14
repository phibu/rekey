---
phase: 01-v1-2-3-hotfix
plan: 01
subsystem: web/smtp
tags: [smtp, tls, security, bug-fix]
requires: []
provides:
  - SmtpSettings.TrustedCertificateThumbprints
  - CertificateTrust.IsTrusted helper
affects:
  - src/PassReset.Web/Models/SmtpSettings.cs
  - src/PassReset.Web/Services/SmtpEmailService.cs
  - src/PassReset.Web/Services/CertificateTrust.cs (new)
  - src/PassReset.Web/appsettings.Production.template.json
  - docs/appsettings-Production.md
  - CHANGELOG.md
tech_stack_added: []
tech_stack_patterns:
  - Pure function extraction for testability (CertificateTrust.IsTrusted)
  - MailKit ServerCertificateValidationCallback with explicit allowlist
key_files_created:
  - src/PassReset.Web/Services/CertificateTrust.cs
key_files_modified:
  - src/PassReset.Web/Models/SmtpSettings.cs
  - src/PassReset.Web/Services/SmtpEmailService.cs
  - src/PassReset.Web/appsettings.Production.template.json
  - docs/appsettings-Production.md
  - CHANGELOG.md
decisions:
  - No global AcceptAllCertificates flag — bypass only via explicit thumbprint allowlist
  - Support both SHA-1 (40 hex) and SHA-256 (64 hex) thumbprints; distinguished by length
  - Tolerate spaces and colons in configured thumbprints; case-insensitive compare
  - Keep MailKit's default CheckCertificateRevocation=true
  - Never log raw certificate data — only thumbprint + subject + SslPolicyErrors
metrics:
  tasks_completed: 3
  tasks_total: 3
  commits: 1
  completed_date: 2026-04-14
requirements:
  - BUG-001
---

# Phase 1 Plan 1: SMTP Internal-CA Trust (BUG-001) Summary

Opt-in SHA-1/SHA-256 thumbprint allowlist via new `SmtpSettings.TrustedCertificateThumbprints`, wired into MailKit's `SmtpClient.ServerCertificateValidationCallback` through a pure, test-ready `CertificateTrust.IsTrusted` helper — enables internal-CA SMTP relays without silent certificate-validation bypass.

## Tasks Completed

| # | Name | Status | Commit |
|---|------|--------|--------|
| 1 | Add TrustedCertificateThumbprints setting + CertificateTrust helper | Done | 8532ff0 |
| 2 | Wire callback into SmtpEmailService + log guardrails | Done | 8532ff0 |
| 3 | Update appsettings template, docs, CHANGELOG | Done | 8532ff0 |

All three tasks combined into a single commit per the plan's primary commit guidance.

## Commit Hashes

- `8532ff0` — `fix(web): support internal-CA SMTP trust via thumbprint allowlist (BUG-001)`

## Files Changed

- **Created:** `src/PassReset.Web/Services/CertificateTrust.cs` — pure helper (no I/O), callable from future xUnit tests.
- **Modified:**
  - `src/PassReset.Web/Models/SmtpSettings.cs` — added nullable `string[]? TrustedCertificateThumbprints`.
  - `src/PassReset.Web/Services/SmtpEmailService.cs` — added `ServerCertificateValidationCallback` delegating to `CertificateTrust.IsTrusted`; error log on failure, warning log when accepted via allowlist.
  - `src/PassReset.Web/appsettings.Production.template.json` — added empty `TrustedCertificateThumbprints: []` under `SmtpSettings`.
  - `docs/appsettings-Production.md` — added SmtpSettings reference-table row describing the new key, defaults, and security posture.
  - `CHANGELOG.md` — added `[Unreleased]` section with BUG-001 Fixed bullet.

## Deviations from Plan

### Workspace state — sibling-plan work in progress

- **Found during:** Task 1 build verification.
- **Issue:** Uncommitted changes from plans 02 (BUG-002) and 03 (BUG-003) were present in the working tree. The plan-02 WIP (`PasswordChangeProvider.cs`, `ApiErrorCode.cs`) had a compile error (`error CS1503` at line 384), which broke `dotnet build` for the whole solution — masking the Release-build verification for plan 01.
- **Resolution:** Temporarily `git stash`-ed the plan-02/03 files (`ApiErrorCode.cs`, `PasswordChangeProvider.cs`, `Install-PassReset.ps1`, `ROADMAP.md`) to verify plan 01 in isolation, committed plan 01 cleanly, then `git stash pop`-ed to return the sibling plans' in-progress files to the working tree for their respective agents/owners.
- **Not fixed by this plan:** The `PasswordChangeProvider.cs` build error is plan 02's responsibility (Rule 4 scope boundary — do not fix pre-existing or sibling-plan issues).
- **Classification:** Tooling/coordination workaround, not a plan deviation in substance.

### TDD deferred (as plan anticipates)

- Task 1 is flagged `tdd="true"`, but xUnit is not yet scaffolded (QA-001, Phase 2). Per plan instruction ("Do NOT block on test infrastructure"), no test file was created. `CertificateTrust.IsTrusted` is pure and test-ready for when the xUnit project lands.

### No state/roadmap updates in this plan

- STATE.md / ROADMAP.md / REQUIREMENTS.md were not updated by this agent because the orchestrator is running plans 01/02/03 in parallel. Progress-bar and requirement-mark-complete operations will be reconciled by the orchestrator after all three plans commit, to avoid write-race on shared files. `state record-session` / `state advance-plan` were therefore skipped here.

## Verification Status

### Automated verification

- `dotnet build src/PassReset.sln --configuration Release` — **PASS**, 0 warnings, 0 errors (verified with sibling-plan files stashed).
- Commit created via `.githooks/commit-msg` format — accepted (`fix(web):` is a valid scope).
- `git grep "TrustedCertificateThumbprints"` — hits in `SmtpSettings.cs`, `SmtpEmailService.cs` (via `_settings.TrustedCertificateThumbprints`), `appsettings.Production.template.json`, `docs/appsettings-Production.md`, `CHANGELOG.md`.
- `grep "return true;" src/PassReset.Web/Services/SmtpEmailService.cs` — no unconditional bypass; only `return trusted;` delegating to `CertificateTrust.IsTrusted`.

### Acceptance criteria (REQUIREMENTS.md BUG-001)

| Criterion | Status |
|-----------|--------|
| Internal-CA SMTP trust via opt-in thumbprint allowlist | Met — `SmtpSettings.TrustedCertificateThumbprints` (SHA-1 or SHA-256) |
| No silent certificate-validation bypass | Met — only explicitly configured thumbprints are accepted; no "trust all" flag |
| Documented in `docs/appsettings-Production.md` | Met — SmtpSettings reference table row added |
| Default behaviour unchanged (no thumbprints → system trust only) | Met — nullable property defaults to null; helper returns false on null/empty allowlist |
| Existing appsettings.Production.json without the new key continues to work | Met — new property is optional/nullable; MailKit falls back to system trust |

### Manual verification (out of scope — staging)

- Pointing at a self-signed SMTP relay: connection should fail (ERROR log with thumbprint + subject).
- After adding the relay's thumbprint to `TrustedCertificateThumbprints`: connection should succeed with one WARNING log per connect.
- Wrong thumbprint configured: ERROR log, no acceptance.

## Known Stubs

None. `CertificateTrust.IsTrusted` is fully implemented; the empty `[]` in the template is the documented default (meaning "system trust only"), not a stub.

## Threat Flags

None. The change narrows attack surface (replaces implicit "rely on OS trust only, fail otherwise" with the same default plus an explicit, audited opt-in). It does not add a new trust boundary.

## Self-Check

- [x] `src/PassReset.Web/Services/CertificateTrust.cs` — FOUND
- [x] `src/PassReset.Web/Models/SmtpSettings.cs` contains `TrustedCertificateThumbprints` — FOUND
- [x] `src/PassReset.Web/Services/SmtpEmailService.cs` contains `ServerCertificateValidationCallback` + `CertificateTrust.IsTrusted` — FOUND
- [x] `src/PassReset.Web/appsettings.Production.template.json` contains `TrustedCertificateThumbprints` — FOUND
- [x] `docs/appsettings-Production.md` contains `TrustedCertificateThumbprints` row — FOUND
- [x] `CHANGELOG.md` `[Unreleased]` contains BUG-001 entry — FOUND
- [x] Commit `8532ff0` — FOUND in `git log`

## Self-Check: PASSED
