---
phase: 08-config-schema-sync
plan: 02
subsystem: ci
tags: [ci, schema, validation, STAB-008]
requires:
  - "08-01 (schema + template already drift-free)"
provides:
  - "CI gate that rejects any PR which breaks schema/template parity"
affects:
  - ".github/workflows/ci.yml"
tech-stack:
  patterns:
    - "pwsh Test-Json -SchemaFile"
    - "GitHub Actions ::error:: workflow annotation"
key-files:
  modified:
    - ".github/workflows/ci.yml"
decisions:
  - "Insert between Restore and Build so schema errors gate the entire pipeline"
  - "Use pwsh Test-Json -ErrorVariable to capture the JSON-path-aware message; echo through ::error:: for PR UX"
  - "Windows-latest runner already in use — no runner change needed"
metrics:
  completed: 2026-04-16
requirements: [STAB-008]
---

# Phase 08 Plan 02: CI Schema Gate Summary

## One-liner
Add a `pwsh` `Test-Json` step to `ci.yml` so CI fails loudly (with JSON-path error) whenever `appsettings.Production.template.json` drifts from `appsettings.schema.json`.

## What shipped

- **New step in `build` job**: `Validate appsettings.Production.template.json against schema`
  - Position: between `Restore .NET dependencies` and `Build solution` (lines 34–54 of `.github/workflows/ci.yml`).
  - Shell: `pwsh` (built-in on `windows-latest`).
  - Uses `Test-Json -Path ... -SchemaFile ... -ErrorVariable errors -ErrorAction SilentlyContinue`.
  - On failure: emits `::error::` annotations (GitHub surfaces them inline in the PR Files view) and `exit 1`.
- Failure halts the rest of the `build` job, which is `needs:`-chained by the `tests` job, so a schema break blocks the entire CI green.

## Verification (must_haves)

| Must-have truth | Status | Evidence |
|-----------------|--------|----------|
| CI runs Test-Json against the template on every push and PR | PASS | Step is in the `build` job, which runs on `push: [master]` and `pull_request: [master]` (ci.yml:3-7). |
| CI fails (non-zero exit) when the template no longer validates | PASS | Offline proof: removing `ClientSettings` root property → `valid=False`, `exit 1` via `if (-not $valid) { … exit 1 }` branch. |
| CI surface includes Test-Json's native error output (line + JSON path) | PASS | `-ErrorVariable errors` captures the full message (e.g. `Required properties ["ClientSettings"] are not present at ''`) and each entry is re-emitted as `::error::` annotation. |

### Deliberate-break proof (offline, equivalent to CI)

Removed `ClientSettings` from a temp copy of the template; ran the same `Test-Json` invocation CI uses:

```
valid=False
errCount=1
err: The JSON is not valid with the schema: Required properties ["ClientSettings"] are not present at ''
```

This confirms the `::error::` path surfaces the offending JSON path. Baseline (unmodified template) returns `valid=True`.

### YAML validity

`python -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml'))"` → exits 0.

### Automated verify command from plan

```pwsh
$valid = Test-Json -Path 'src/PassReset.Web/appsettings.Production.template.json' -SchemaFile 'src/PassReset.Web/appsettings.schema.json'
# → True
```

## Acceptance criteria audit

- [x] `grep -c 'Validate appsettings.Production.template.json against schema' ci.yml` → 1
- [x] `grep -c 'Test-Json' ci.yml` → 1 (inside new step)
- [x] `grep -c 'shell: pwsh' ci.yml` → 1 (new step)
- [x] Success path exit 0 (local `Test-Json` returns `True`)
- [x] Deliberate-break returns non-zero with `::error::` annotations
- [x] YAML parses
- [x] Step runs on `windows-latest` (inherited from `build` job)

## Commits

- `e47c026` — ci(deploy): validate appsettings template against schema in CI

## Deviations from Plan

None — plan executed as written. The prior executor’s uncommitted YAML block matched the exact pattern prescribed in 08-PATTERNS; no edits required before committing.

## Threat model disposition

- T-08-04 (workflow tampering): accept — repo PR review gate.
- T-08-05 (silent skip): mitigated — step is unconditional (no `if:`), runs before build, failure halts pipeline.
- T-08-06 (schema weakening via PR): accept — visible in PR review.

## Files Modified

- `.github/workflows/ci.yml` (+22 lines, 0 deletions)

## Self-Check: PASSED

- FOUND: `.github/workflows/ci.yml` — contains `Test-Json` step on line 34–54.
- FOUND commit: `e47c026` — visible in `git log --oneline -1`.
- FOUND proof: `Test-Json` returns `True` on current template; `False` + JSON-path error when `ClientSettings` removed.
