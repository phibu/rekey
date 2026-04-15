---
status: partial
phase: 01-v1-2-3-hotfix
source:
  - .planning/phases/01-v1-2-3-hotfix/01-SUMMARY.md
  - .planning/phases/01-v1-2-3-hotfix/02-SUMMARY.md
  - .planning/phases/01-v1-2-3-hotfix/03-SUMMARY.md
started: 2026-04-15T00:00:00Z
updated: 2026-04-15T00:00:00Z
---

## Current Test

[testing paused by user — 5 tests unresolved]

## Tests

### 1. Cold Start Smoke Test
expected: Kill any running PassReset.Web instance. Start fresh via `dotnet run --project src/PassReset.Web` (or IIS site recycle). Server boots with 0 errors, `GET /api/health` returns 200, and `GET /api/password` returns ClientSettings JSON including the new `Alerts.ErrorPasswordTooRecentlyChanged` key.
result: skipped

### 2. BUG-001 — Default SMTP trust unchanged
expected: With `SmtpSettings.TrustedCertificateThumbprints` unset (or empty `[]`), an SMTP send to a relay signed by an untrusted/internal CA fails and an ERROR log is emitted containing the thumbprint, subject, and SslPolicyErrors. No silent bypass.
result: pass

### 3. BUG-001 — Thumbprint allowlist accepts internal-CA relay
expected: Add the internal-CA SMTP relay's SHA-1 or SHA-256 thumbprint (spaces/colons/case all tolerated) to `SmtpSettings.TrustedCertificateThumbprints`. Email send succeeds; one WARNING log per connect records thumbprint + subject. Wrong thumbprint → ERROR, no send.
result: [pending]

### 4. BUG-002 — PasswordTooRecentlyChanged surfaces dedicated alert
expected: Submit a password change for a user whose last change falls inside the domain `minPwdAge` window. UI shows the `ErrorPasswordTooRecentlyChanged` message (from ClientSettings), NOT the generic "Unexpected Error". Server log contains a warning with the HResult and AD message. SetPassword fallback is NOT invoked.
result: [pending]

### 5. BUG-003 — Upgrade preserves custom AppPool service account
expected: On a host where the PassReset IIS AppPool currently runs as `CORP\svc-passreset` (SpecificUser), run `Install-PassReset.ps1` as an upgrade without `-AppPoolIdentity`. After install: AppPool still runs as `CORP\svc-passreset` (identityType unchanged), NTFS ACLs on the install dir grant Read/Execute to `CORP\svc-passreset` (not `IIS AppPool\<poolname>`), and the installer logs a preserve notice.
result: [pending]

### 6. BUG-003 — Fresh install defaults to ApplicationPoolIdentity
expected: On a host with no pre-existing PassReset AppPool, run `Install-PassReset.ps1` without `-AppPoolIdentity`. New AppPool is created with identityType `ApplicationPoolIdentity` (4); ACLs granted to `IIS AppPool\<poolname>`.
result: [pending]

### 7. BUG-003 — Explicit `-AppPoolIdentity` override still works
expected: Run `Install-PassReset.ps1 -AppPoolIdentity CORP\svc-other -AppPoolPassword ...` as an upgrade. Pool switches to `CORP\svc-other`; ACLs regranted to the new account.
result: [pending]

## Summary

total: 7
passed: 1
issues: 0
pending: 5
skipped: 1

## Gaps

[none yet]
