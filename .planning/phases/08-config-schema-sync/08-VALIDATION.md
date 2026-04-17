---
phase: 08
slug: config-schema-sync
status: reconstructed
nyquist_compliant: false
wave_0_complete: true
created: 2026-04-17
covered: 4
partial: 0
missing: 2
manual_only: 2
---

# Phase 08 — Validation Strategy (reconstructed)

> Nyquist auditor reconstruction for completed phase. No VALIDATION.md existed
> at execution time; this artifact documents the actual test coverage plus
> gap-fill tests added during audit (2026-04-17).

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework (backend)** | xUnit v3 (`xunit.v3 3.2.2`) via `Microsoft.NET.Test.Sdk` |
| **Framework (frontend)** | Vitest + RTL — NOT applicable to phase 08 (backend/installer only) |
| **Config file** | `src/PassReset.Tests/PassReset.Tests.csproj` (`IsTestProject=true`, `OutputType=Exe`) |
| **Quick run command (phase 08 scope)** | `dotnet test src/PassReset.sln --configuration Release --filter "FullyQualifiedName~Validator\|FullyQualifiedName~Startup\|FullyQualifiedName~SchemaArtifact\|FullyQualifiedName~StartupValidationFailureLogger"` |
| **Full suite command** | `dotnet test src/PassReset.sln --configuration Release` |
| **Estimated runtime (phase 08 filter)** | ~45 ms for the 10 gap-fill tests; full validator+startup suite completes in < 5 s |
| **Installer tests** | NO Pester harness in `deploy/`. PowerShell sync/drift behaviour is smoke-tested in-session only. |

---

## Sampling Rate

- **After every task commit:** `dotnet test --filter "FullyQualifiedName~<changed_type>"`
- **After every plan wave:** `dotnet test src/PassReset.sln --configuration Release`
- **Before release:** Full suite green + CI `Test-Json` gate green
- **Max feedback latency:** ~5 seconds for phase 08 validators

---

## Per-Task Verification Map

| Task ID | Plan | Requirement | Secure Behavior | Test Type | Automated Command | File | Status |
|---------|------|-------------|-----------------|-----------|-------------------|------|--------|
| 08-01 | 01 | STAB-007 (template pure JSON) | Template parses as strict JSON with zero `//` comment lines. | unit (parse) | `dotnet test --filter "FullyQualifiedName~SchemaArtifactTests.Template_Has_Zero_Json_Comments"` | `src/PassReset.Tests/Web/Startup/SchemaArtifactTests.cs` | ✅ green |
| 08-01 | 01 | STAB-008 (authoritative schema) | Schema is valid JSON Schema Draft 2020-12; declares all seven required top-level sections; uses only D-04-permitted keywords. | unit (parse+assert) | `dotnet test --filter "FullyQualifiedName~SchemaArtifactTests"` | `src/PassReset.Tests/Web/Startup/SchemaArtifactTests.cs` | ✅ green |
| 08-02 | 02 | STAB-008 (CI gate) | CI fails when template no longer validates against schema via `Test-Json`. | integration (CI) | `.github/workflows/ci.yml` → "Validate appsettings.Production.template.json against schema" step | `.github/workflows/ci.yml` | ✅ green (commit e47c026) |
| 08-03 | 03 | STAB-009 (runtime validation) | All 7 options classes validate at `builder.Build()` via `ValidateOnStart()`; failures throw `OptionsValidationException`; secret fields redacted. | integration + unit | `dotnet test --filter "FullyQualifiedName~StartupValidationTests"` | `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` (13 tests) | ✅ green |
| 08-03 | 03 | STAB-009 (Event Log logger) | `StartupValidationFailureLogger.LogToEventLog` never throws even when source unregistered (must not mask original exception). | unit (reflection) | `dotnet test --filter "FullyQualifiedName~StartupValidationFailureLoggerTests"` | `src/PassReset.Tests/Web/Services/StartupValidationFailureLoggerTests.cs` | ✅ green |
| 08-04 | 04 | STAB-009 / STAB-011 (installer scaffolding) | `Install-PassReset.ps1` requires pwsh 7, registers Event Log source, runs `Test-Json` pre-flight, exposes `-ConfigSync`. | manual / smoke | pwsh parser check + grep audits documented in 08-04-SUMMARY | `deploy/Install-PassReset.ps1` | ⚠️ manual-only |
| 08-05 | 05 | STAB-010 (additive-merge sync) | Sync walks schema, adds missing keys with defaults, NEVER modifies existing values, arrays atomic, obsolete keys reported. | manual / in-session smoke | Smoke test documented in 08-05-SUMMARY (3 scenarios PASS) | `deploy/Install-PassReset.ps1` (`Sync-AppSettingsAgainstSchema`) | ⚠️ manual-only |
| 08-06 | 06 | STAB-012 (drift check) | Drift check reads schema, always runs on upgrade, never mutates config, reports missing/obsolete/unknown. | manual / in-session smoke | Smoke test documented in 08-06-SUMMARY (5 scenarios PASS) | `deploy/Install-PassReset.ps1` (`Test-AppSettingsSchemaDrift`) | ⚠️ manual-only |
| 08-07 | 07 | STAB-008 (publish packaging) | `Publish-PassReset.ps1` copies schema into publish output; pre-publish `Test-Json` gate blocks release on template-schema drift. | CI | ci.yml schema-validation step + release.yml publish gate | `deploy/Publish-PassReset.ps1` | ✅ green |
| 08-08 | 08 | STAB-007..012 (docs) | CHANGELOG + UPGRADING + docs/ record all six STAB entries with correct operator runbooks. | static (grep) | `grep -c "STAB-00[7-9]\|STAB-01[0-2]" CHANGELOG.md` ≥ 6 | `CHANGELOG.md`, `UPGRADING.md`, `docs/*.md` | ✅ green |

*Status: ✅ green · ❌ red · ⚠️ manual-only · ⬜ pending*

---

## Wave 0 Requirements

Existing infrastructure covers all automated requirements. Gap-fill added during audit:

- [x] `src/PassReset.Tests/Web/Startup/SchemaArtifactTests.cs` — 7 tests for STAB-007/008 schema invariants
- [x] `src/PassReset.Tests/Web/Services/StartupValidationFailureLoggerTests.cs` — 2 tests for STAB-011 best-effort contract
- [x] Existing `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` — 13 tests covering all 7 validators + fail-fast integration (WR-01 landed pre-audit)

---

## Manual-Only Verifications

PowerShell installer behaviour has no Pester harness in `deploy/` and the public
surfaces (Test-Json pre-flight, `-ConfigSync Merge/Review/None`, drift reporting)
require a Windows + IIS + pwsh 7 host to exercise end-to-end.

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Schema-driven additive-merge sync preserves operator overrides + array atomicity | STAB-010 | No Pester harness; function requires `Install-PassReset.ps1` elevated + live IIS site | On a test VM: (1) `Install-PassReset.ps1 -ConfigSync None` to establish baseline; (2) edit `appsettings.Production.json` to override `WebSettings.EnableHttpsRedirect=false` and add a custom element to `AllowedAdGroups`; (3) run upgrade `Install-PassReset.ps1 -Force` (defaults to Merge); (4) verify: overridden value preserved, array unchanged, schema-only keys added with defaults. 08-05-SUMMARY smoke test is the reference procedure. |
| Schema-drift check reports missing / obsolete / unknown keys unconditionally on upgrade | STAB-012 | Requires `$siteExists` branch to trigger; needs pwsh 7 + admin + `appsettings.schema.json` + live config on disk | On a test VM after completing STAB-010 smoke above: remove one required key from the live config, run upgrade with `-ConfigSync None`, confirm drift report lists the removed key AND the hint "Re-run with -ConfigSync Merge". 08-06-SUMMARY documents 5 smoke scenarios as the reference. |
| `Install-PassReset.ps1` pre-flight `Test-Json` halts install on invalid live config | STAB-009 (install-time half) | Requires pwsh 7 + elevated + live config | On a test VM: corrupt `appsettings.Production.json` (e.g., set `WebSettings.EnableHttpsRedirect="not-a-bool"`); re-run `Install-PassReset.ps1`; verify it aborts with the D-08-style error message and does not mutate the file. |
| Event Log source `PassReset` registered by installer and event ID 1001 surfaces `OptionsValidationException` detail | STAB-011 | Requires admin + Windows Event Viewer to observe end-to-end | (1) Run installer on clean VM, confirm `[System.Diagnostics.EventLog]::SourceExists('PassReset')` returns True. (2) Break the config (e.g., invalid `LdapPort`), restart app pool. (3) Open Event Viewer → Windows Logs → Application; filter source `PassReset` event ID 1001; confirm entry appears with D-08 formatted failure and no secret values echoed. |

---

## Validation Sign-Off

- [x] All tasks have `<automated>` verify, CI gate, or documented manual-only procedure
- [x] Sampling continuity: no phase-08 automated test gap longer than 1 plan (08-04/05/06 installer trio is the only manual-only cluster; 08-01/02/03/07/08 have CI + xUnit coverage)
- [x] Wave 0 covers all automated MISSING references (schema artifact + logger contract filled during audit)
- [x] No watch-mode flags
- [x] Feedback latency < 5 s (phase 08 filter) / < 60 s (full suite)
- [ ] `nyquist_compliant: true` — **NOT set** because STAB-010 and STAB-012 installer behaviour remain manual-only (no Pester harness). The four manual-only gates are documented above with explicit reproduction procedures.

**Approval:** reconstructed 2026-04-17 (nyquist auditor pass)

---

## Audit Trail

| Date | Action | Artifact |
|------|--------|----------|
| 2026-04-16 | Phase 08 executed (plans 01–08); no VALIDATION.md produced at execution time. | `.planning/phases/08-config-schema-sync/08-*-SUMMARY.md` |
| 2026-04-16 | Existing `StartupValidationTests.cs` landed (WR-01) covering all 7 validators. | `src/PassReset.Tests/Web/Startup/StartupValidationTests.cs` |
| 2026-04-17 | Nyquist audit reconstruction. Added 2 test files (9 new tests) to close automated gaps for STAB-007, STAB-008 schema artifact invariants, and STAB-011 Event Log logger contract. | `src/PassReset.Tests/Web/Startup/SchemaArtifactTests.cs`, `src/PassReset.Tests/Web/Services/StartupValidationFailureLoggerTests.cs` |
