---
phase: 08-config-schema-sync
plan: 06
subsystem: installer
tags: [installer, drift-check, schema, diagnostic, stab-012]
requires: [08-05]
provides:
  - "Test-AppSettingsSchemaDrift function in deploy/Install-PassReset.ps1 (diagnostic-only)"
  - "Unconditional drift-check dispatch block that runs on every upgrade after sync"
affects: [deploy/Install-PassReset.ps1]
tech-stack:
  added: []
  patterns:
    - "D-17 schema-as-truth — drift walker reads appsettings.schema.json (not the template)"
    - "D-18 always-runs — drift check has NO outer try/catch and NO parse-success skip branch"
    - "Helper reuse — Get-SchemaKeyManifest / Get-LiveValueAtPath from plan 08-05, zero duplicated walker code"
    - "Purely diagnostic — function contains no Set-Content / ConvertTo-Json / Out-File (T-08-25 mitigation)"
    - "Post-sync placement — drift runs AFTER sync so 'Missing' only surfaces when sync=None or schema had no default"
key-files:
  created: []
  modified:
    - deploy/Install-PassReset.ps1
decisions:
  - "Unknown top-level keys are informational only (DarkGray) — schema permits additionalProperties, so custom operator-added sections must not trigger warnings"
  - "No try/catch around ConvertFrom-Json inside the drift function — D-18 explicitly forbids silent-skip on parse success; if parsing fails, 08-04 pre-flight missed it and the operator needs the exception"
  - "Drift dispatch gated on $siteExists only (not $siteExists -and Test-Path $prodConfig) — the function itself handles missing files via Write-Warn + Skipped=true, so the upgrade path gets diagnosis even when the config file vanished"
requirements: [STAB-012]
metrics:
  duration: ~15min
  completed: 2026-04-16
---

# Phase 08 Plan 06: Schema-drift check (diagnostic-only) Summary

STAB-012 — the legacy drift walker (removed by plan 08-05) silently skipped the schema pass whenever the live `appsettings.Production.json` parsed successfully, leaving operators blind to missing-key drift. New `Test-AppSettingsSchemaDrift` runs unconditionally on every upgrade, reads `appsettings.schema.json` as the authoritative source of required keys (D-17), reports missing / obsolete / unknown keys, and never writes to disk. Mutation is sync's job (plan 08-05).

## What Was Built

### Drift function (Install-PassReset.ps1 lines ~324-380)

Placed immediately after `Sync-AppSettingsAgainstSchema` so it shares the helper-block header comment.

| Returned field | Meaning |
|----------------|---------|
| `Missing`  | Array of manifest entries for schema-required keys absent from live config (after sync) |
| `Obsolete` | Array of manifest entries flagged `x-passreset-obsolete: true` that are still present |
| `Unknown`  | Array of top-level key names present in live config but not in `schema.properties` (informational only — `additionalProperties: true` allows them) |
| `Skipped`  | `$true` only when schema or config file doesn't exist (already warned inside function) |

Body reuses `Get-SchemaKeyManifest` (schema walk) and `Get-LiveValueAtPath` (safe path lookup) from plan 08-05 — zero duplicated traversal logic.

### Dispatch block (Install-PassReset.ps1 lines ~1047-1085)

Positioned AFTER the sync dispatch (plan 08-05) so the report reflects the post-sync state. Gated only on `$siteExists` — upgrades always trigger the check.

Operator-actionable messages:

```
Schema drift: N required key(s) still missing from C:\...\appsettings.Production.json:
    - PasswordChangeOptions:LdapPort (schema default: 636)
    - SmtpSettings:Password (no default in schema; manual entry required)
Re-run with -ConfigSync Merge to add missing keys automatically.

Schema drift: N obsolete key(s) present in C:\...\appsettings.Production.json:
    - Legacy:Flag (obsolete since v1.3.2)
Re-run with -ConfigSync Review to remove obsolete keys interactively.

  [i] 1 unknown top-level key(s) in C:\...\appsettings.Production.json (allowed; informational only):
    - MyCustomSection

OK: No schema drift detected.
```

If `-ConfigSync None` was used and the drift report finds missing keys, the operator gets a hint to re-run with `-ConfigSync Merge`. If obsoletes are found, hint points to `-ConfigSync Review` for interactive removal.

## Smoke Test Results

Ran against real `src/PassReset.Web/appsettings.schema.json` via a dot-source extraction of the helper + drift function blocks (parser-extracted from the installer, evaluated in a fresh pwsh session with `Write-Ok`/`Write-Warn`/`Write-Step` shims). 5 scenarios, all PASS:

| # | Scenario | Expected | Actual |
|---|----------|----------|--------|
| 1 | Clean config — every defaulted required key populated via `Set-LiveValueAtPath` | `Missing=0 Obsolete=0 Unknown=0 Skipped=False` | `Missing=0 Obsolete=0 Unknown=0 Skipped=False` ✓ |
| 2 | Clean config minus one defaulted required key (`WebSettings:EnableHttpsRedirect` removed via `Remove-LiveValueAtPath`) | `Missing>=1` and includes removed path | `Missing=1` and list contains `WebSettings:EnableHttpsRedirect` ✓ |
| 3 | Synthetic mini-schema with `LegacyFlag` marked `x-passreset-obsolete` + live config containing `LegacyFlag` | `Obsolete>=1` with `LegacyFlag` | `Obsolete=1`, path `LegacyFlag` ✓ |
| 4 | Clean config + extra top-level `MyCustomSection` | `Unknown` includes `MyCustomSection` | `Unknown=1` with `MyCustomSection` ✓ |
| 5 | Byte-level file comparison before/after drift check (mutation guard) | file unchanged | byte-identical (T-08-25 mitigation verified) ✓ |

Observed during Test 1 — every non-obsolete schema leaf has either a `default` or gets a `<placeholder>` assigned during test-fixture construction; this matches what `Sync-AppSettingsAgainstSchema` would produce on upgrade after operator secrets are set, confirming the drift check reports "no drift" in the expected steady state.

## Acceptance Criteria

| Criterion | Result |
|-----------|--------|
| `grep -c 'function Test-AppSettingsSchemaDrift'` returns 1 | PASS (1) |
| `grep -c 'Checking appsettings.Production.json for schema drift'` returns 1 | PASS (1) |
| Drift function uses `Get-SchemaKeyManifest` | PASS (2 matches: def + drift call) |
| Drift dispatch line > Sync dispatch line | PASS (post-sync placement verified) |
| Drift function never calls `Set-Content` / `ConvertTo-Json` / `Out-File` | PASS (regex scan of function body) |
| No silent-skip try/catch in dispatch block | PASS (regex scan of dispatch block) |
| `pwsh Parser::ParseFile` exits 0 | PASS (PARSE OK) |
| Smoke test: clean config → `No schema drift detected.` | PASS (Test 1) |
| Smoke test: post-sync missing required key → `Missing.Count >= 1` + warning surfaced | PASS (Test 2) |

## Must-Haves (from plan frontmatter)

| Truth | Verified via |
|-------|--------------|
| Drift check reads the SCHEMA (not the template) for required keys (D-17) | Function body calls `Get-SchemaKeyManifest -Schema $schema` where `$schema = Get-Content $SchemaPath -Raw \| ConvertFrom-Json`. No template reference. |
| Drift check ALWAYS runs on upgrade (D-18) | Dispatch gated on `$siteExists` only. No parse-success short-circuit. Verification script asserts no try/catch in dispatch block. |
| Drift check reports missing / obsolete / unknown | Smoke tests 2, 3, 4. |
| Drift check NEVER mutates the file | Smoke test 5 (byte compare) + grep scan of function body. |
| Drift check runs AFTER sync | Verification script asserts `driftDispatch > syncDispatch` in source text. |

## Files Modified

- `deploy/Install-PassReset.ps1` — +107 LOC (function block at ~line 324, dispatch block at ~line 1047). No deletions.

## Deviations from Plan

### Unknown-key detection scoped to TOP level only

Plan behavior spec says "Unknown top-level keys (present in live but not in schema's `properties` at root)". Implementation matches that exactly — deep walking for unknowns would conflict with `additionalProperties: true` and could flood the operator with noise (e.g., operator-added nested metadata). Top-level scope is the minimum useful signal without false positives. Documented here for traceability; not a deviation from plan language but worth noting the boundary decision.

### Dispatch line numbers

Plan 08-06's action step placed the dispatch block inline; post-edit the line positions are function at 324–380, dispatch at 1047–1085. Not a deviation — just a concrete line-number recording for any follow-up that needs to locate the block.

## Auth Gates

None.

## Known Stubs

None. Drift check is fully operational and consumed by the upgrade path.

## Lessons / Follow-ups

### For 08-08 (documentation)

`UPGRADING.md` should document the new drift report format and cross-reference the `-ConfigSync` remediation hints. Add a troubleshooting entry: "Upgrade prints 'Schema drift: N required key(s) missing' — what do I do?" → answer: re-run with `-ConfigSync Merge`, or for per-key review `-ConfigSync Review`.

### Schema obsolete markers

No schema entries currently carry `x-passreset-obsolete: true` (confirmed during smoke Test 3, which had to build a synthetic schema). When the first real deprecation lands, the drift check will automatically surface it on upgrade without any installer changes — the mechanism is in place.

### Reusable pattern

`Get-SchemaKeyManifest` + `Get-LiveValueAtPath` have now been consumed by two consecutive plans (08-05 sync, 08-06 drift) with zero duplication. That pair is the right primitive for any future schema-diagnostic work (validation reports, `-WhatIf` previews, etc.).

## Self-Check: PASSED

- FOUND: `deploy/Install-PassReset.ps1` (modified; function + dispatch both present)
- FOUND commit: `f43da37` (feat(installer): add schema-driven drift check (08-06))
- FOUND: all 5 must_haves truths (verified via parser checks + 5/5 smoke tests)
- FOUND: key_links patterns
  - `Get-SchemaKeyManifest` reused (appears in both sync and drift bodies)
  - "no .silently skip. branch when live parses" — verification script confirms drift dispatch block contains no try/catch
- Parser check: PARSE OK under pwsh 7
- Smoke test: 5/5 PASS
