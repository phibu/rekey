---
phase: 02-v1-3-test-foundation
plan: 05
subsystem: ci
tags: [github-actions, ci, release, workflow_call]
requires: ["02-02", "02-04"]
provides:
  - "Reusable .github/workflows/tests.yml run by both ci.yml and release.yml"
  - "ci.yml gates tests after build (needs: build → tests)"
  - "release.yml gates publish on test pass (needs: tests)"
  - "Coverage artifacts uploaded with 30-day retention"
affects:
  - .github/workflows/tests.yml
  - .github/workflows/ci.yml
  - .github/workflows/release.yml
  - CHANGELOG.md
key_files_created:
  - .github/workflows/tests.yml
  - .planning/phases/02-v1-3-test-foundation/02-05-SUMMARY.md
key_files_modified:
  - .github/workflows/ci.yml
  - .github/workflows/release.yml
  - CHANGELOG.md
  - CLAUDE.md  # local only — gitignored, not committed
decisions:
  - "Publish-PassReset.ps1 already publishes src/PassReset.Web/PassReset.Web.csproj directly — test project naturally excluded from release zip. No script change needed."
  - "tests.yml passes -p:CollectCoverage=true explicitly to dotnet test (csproj only enables coverage under that condition, so CI must opt in)."
  - "ci.yml runs tests AFTER build (needs: build) — keeps the build-only signal fast and gives test failures their own status check."
  - "release.yml runs tests BEFORE publish (needs: tests on the release job) — a failing test prevents the zip from being built or uploaded."
  - "CLAUDE.md was updated locally but NOT committed (file is .gitignored per its own front matter)."
metrics:
  tasks_completed: 3
  files_created: 1
  files_modified: 3
  completed: "2026-04-15"
deviations:
  - "CHANGELOG note quotes 20%/20% threshold (the achieved baseline) rather than the plan's 55%/45% target — see 02-02 SUMMARY for context."
  - "CLAUDE.md edit applied locally but not committed (gitignored)."
---

# Phase 02 Plan 05: CI Test Gate Summary

## What was built

- **`.github/workflows/tests.yml`** — reusable `workflow_call` workflow that on `windows-latest`:
  - sets up .NET 10 and Node 22 (matching ci.yml versions)
  - runs `dotnet test src/PassReset.sln --configuration Release --no-restore -p:CollectCoverage=true`
  - runs `npm run test:coverage` in `src/PassReset.Web/ClientApp`
  - uploads `backend-coverage` and `frontend-coverage` artifacts with 30-day retention (always — even on failure)

- **`.github/workflows/ci.yml`** — added `tests` job after the existing `build` job:
  ```yaml
  tests:
    needs: build
    uses: ./.github/workflows/tests.yml
  ```

- **`.github/workflows/release.yml`** — added a `tests` job and made the existing `release` job depend on it:
  ```yaml
  jobs:
    tests:
      uses: ./.github/workflows/tests.yml
    release:
      needs: tests
      ...
  ```

- **CHANGELOG.md `[Unreleased]`** — added Added/Changed entries documenting the test foundation, refactors, and CI gate (all tagged `(QA-001)`).

- **CLAUDE.md** — locally updated build commands to include `dotnet test` and `npm test`, and replaced the "no automated tests" line. NOT committed (file is gitignored).

## Verification

- `tests.yml` contains `workflow_call` ✓
- `ci.yml` references `uses: ./.github/workflows/tests.yml` ✓
- `release.yml` references `uses: ./.github/workflows/tests.yml` and the `release` job has `needs: tests` ✓
- `Publish-PassReset.ps1:108` calls `dotnet publish src\PassReset.Web\PassReset.Web.csproj` — test project naturally excluded ✓
- `CHANGELOG.md` contains `QA-001` entries ✓
