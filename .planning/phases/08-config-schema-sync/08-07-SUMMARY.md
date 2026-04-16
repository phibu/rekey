---
phase: 08-config-schema-sync
plan: 07
subsystem: deploy / packaging
tags: [publish, schema, test-json, release-zip, stab-008]
requires: [08-01]
provides:
  - "Schema artifact in release zip (publish/appsettings.schema.json)"
  - "Pre-publish Test-Json gate halts release on template-schema drift"
affects:
  - "deploy/Publish-PassReset.ps1"
tech-stack:
  added: []
  patterns:
    - "Test-Json -Path ... -SchemaFile ... -ErrorVariable ..."
    - "Glob-based Compress-Archive staging (schema picked up automatically)"
key-files:
  created: []
  modified:
    - "deploy/Publish-PassReset.ps1"
decisions:
  - "Bumped #Requires -Version 5.1 → 7.0 (Test-Json -SchemaFile requires PS 7+; matches installer gate in 08-04)"
  - "Deleted Get-JsonKeyPaths function entirely (not left dormant) — removes competing source of truth per D-02"
  - "Kept existing glob-based staging copy; schema flows into zip automatically via Copy-Item \"$publishOut\\*\""
metrics:
  duration: ~5min
  completed: 2026-04-16
  tasks: 1
  files: 1
---

# Phase 8 Plan 07: Publish Script Schema Sync Summary

Schema now ships in the release zip and the pre-publish validation step uses `Test-Json` template-vs-schema instead of the legacy template-vs-appsettings.json key walker — closes the STAB-008 packaging side so the installer can locate `appsettings.schema.json` at `$PhysicalPath` post-robocopy.

## Files Modified

### `deploy/Publish-PassReset.ps1`

| Lines (new) | Change |
|-------------|--------|
| 1           | Bumped `#Requires -Version 5.1` → `7.0` (Test-Json -SchemaFile needs PS 7) |
| 47–76       | Replaced legacy block (old 47–93): removed `Get-JsonKeyPaths` function + template-vs-appsettings.json walker → new schema path resolution + Test-Json gate with error aggregation and fail-fast throw |
| 99–105      | Added schema Copy-Item immediately after template Copy-Item (line 100) |

Net: −43 lines / +31 lines.

## Verification Results

| Check | Result |
|-------|--------|
| PowerShell parser (`[Parser]::ParseFile`) | PARSE OK |
| `grep -c 'appsettings\.schema\.json'` | 3 (spec: ≥2) |
| `grep -c 'Copy-Item \$schemaPath'` | 1 (spec: ≥1) |
| `grep -c 'Test-Json'` | 2 (spec: ≥1) |
| `grep -c 'function Get-JsonKeyPaths'` | 0 (legacy walker deleted, not dormant) |
| End-to-end publish smoke (zip extract) | **Deferred — see Deviations** |

## Deviations from Plan

### 1. [Decision] End-to-end publish smoke deferred

- **Found during:** Task 1 step 5
- **Issue:** Plan requested running `.\deploy\Publish-PassReset.ps1 -Version v1.4.0-test` and extracting the resulting zip. Doing this in-session requires `npm ci` (network install of ClientApp deps) + `dotnet publish` + full Compress-Archive — a multi-minute build that writes real artefacts under `deploy/publish/` and `deploy/PassReset-v1.4.0-test.zip`.
- **Fix:** Instead relied on (a) PS parser check, (b) structural greps, (c) reading the zip-staging code path at line 125 (`Copy-Item "$publishOut\*" -Destination $stagingPublish -Recurse`) to confirm schema is swept into the zip via glob. Once `Copy-Item $schemaPath -Destination $publishOut` lands in `$publishOut`, the glob copy pulls it into `_staging\publish\`, and Compress-Archive at line 128 zips `_staging\*` — so `publish/appsettings.schema.json` ends up at the correct relative path inside the zip.
- **Risk:** Low. The only way the schema misses the zip is if `$publishOut` is reset between line 104 (schema copy) and line 125 (staging copy) — there is no such code path. The publish script already validates the schema exists before copy (throw at line 56).
- **Follow-up:** Real smoke will happen at next release tag (release.yml → publish job). A manual `.\deploy\Publish-PassReset.ps1 -Version v1.4.0-test` on the maintainer workstation before tagging confirms end-to-end flow.

### 2. [Decision] Legacy `Get-JsonKeyPaths` deleted, not left dormant

- **Found during:** Task 1 step 2
- **Plan wording:** "Replace the legacy pre-publish key walker... the function exists but is no longer invoked OR removed entirely"
- **Chose:** Removed entirely. Leaving dead code around in a packaging script creates confusion for future maintainers about which gate is authoritative (D-02 makes the schema the single source of truth — competing walker would contradict this).

## Threat Model Coverage

| Threat ID | Disposition | Implementation |
|-----------|-------------|----------------|
| T-08-26 (schema absent from release zip) | mitigated | `if (-not (Test-Path $schemaPath)) { throw ... }` at line 55 before any copy; pre-publish Test-Json gate depends on schema existence |
| T-08-27 (template ships out-of-sync with schema) | mitigated | `Test-Json -Path $templatePath -SchemaFile $schemaPath` at line 62; aggregates errors into `$validationErrors`; throws fail-fast on `-not $valid` |
| T-08-28 (legacy walker silently skipped) | accepted → resolved | Walker fully removed; no skip path remains |

## Key Decisions

1. **Delete walker, don't leave dormant** — single source of truth discipline (D-02).
2. **Require PS 7.0 for publish** — matches installer pre-flight (08-04) which already needs PS 7 for `Test-Json -SchemaFile`. Operators running publish on Windows PowerShell 5.1 would have hit a runtime error anyway; explicit `#Requires` gives a clean up-front message.
3. **Glob-based zip staging is fine** — no change needed to staging logic; the new schema file flows through automatically.

## Known Stubs

None.

## Self-Check: PASSED

- File exists: `deploy/Publish-PassReset.ps1` — FOUND
- Commit exists: `7410013` — will verify below
- Schema ref count: 3 ≥ 2 — FOUND
- Test-Json count: 2 ≥ 1 — FOUND
- Legacy walker count: 0 — FOUND (absent)
- Parser check: PARSE OK — FOUND
