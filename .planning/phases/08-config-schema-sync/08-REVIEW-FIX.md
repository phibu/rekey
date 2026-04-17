---
phase: 08-config-schema-sync
fixed_at: 2026-04-17T00:00:00Z
status: all_fixed
fix_scope: critical_warning
findings_in_scope: 4
fixed: 4
skipped: 0
iteration: 1
---

# Phase 08 Code Review Fix Report

## Summary

All 4 warnings from 08-REVIEW.md resolved. Info findings (IN-01..IN-07) intentionally out of scope.

## Fixes

### WR-01 — Startup test coverage for six validators
- Files: `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs`
- Commit: 2f1802e
- Outcome: fixed
- Notes: added Success + targeted Fail tests for ClientSettings, WebSettings, SmtpSettings, SiemSettings, EmailNotification, PasswordExpiryNotification validators. All 133 tests pass (121 original + 12 new).

### WR-02 — Accumulate PasswordChangeOptionsValidator errors
- Files: `src/PassReset.PasswordProvider/PasswordChangeOptionsValidator.cs`
- Commit: (earlier in phase)
- Outcome: fixed
- Notes: refactored to List<string> accumulation matching peer validators

### WR-03 — LdapHostnames schema default
- Files: `src/PassReset.Web/appsettings.schema.json`
- Commit: b981a9c
- Outcome: fixed
- Notes: default changed from [""] to []

### WR-04 — StrictMode-safe obsolete-key probe
- Files: `deploy/Install-PassReset.ps1`
- Commit: 874e2ef
- Outcome: fixed
- Notes: wrapped `$node.'x-passreset-obsolete'` in `PSObject.Properties.Name -contains` gate inside `Get-SchemaKeyManifest`; also gated `x-passreset-obsolete-since` and `default` property access for StrictMode safety.
