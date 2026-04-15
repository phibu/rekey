---
phase: 02-v1-3-test-foundation
plan: 03
subsystem: frontend-test-foundation
tags: [vitest, jsdom, react-testing-library, coverage, clientapp]
requires: []
provides:
  - Vitest runner in ClientApp (jsdom env, v8 coverage)
  - npm test / test:watch / test:coverage scripts
  - @testing-library/jest-dom matchers registered globally
affects:
  - src/PassReset.Web/ClientApp/package.json
  - src/PassReset.Web/ClientApp/tsconfig.json
tech_stack_added:
  - vitest@4.1.4
  - "@vitest/coverage-v8@4.1.4"
  - jsdom@29.0.2
  - "@testing-library/react@16.3.2"
  - "@testing-library/user-event@14.6.1"
  - "@testing-library/jest-dom@6.9.1"
patterns:
  - Coverage thresholds declared in vitest.config.ts (50/40/50/50)
  - jest-dom matchers loaded via setupFiles import
key_files_created:
  - src/PassReset.Web/ClientApp/vitest.config.ts
  - src/PassReset.Web/ClientApp/vitest.setup.ts
  - src/PassReset.Web/ClientApp/src/smoke.test.ts
  - src/PassReset.Web/ClientApp/.gitignore
key_files_modified:
  - src/PassReset.Web/ClientApp/package.json
  - src/PassReset.Web/ClientApp/package-lock.json
  - src/PassReset.Web/ClientApp/tsconfig.json
decisions:
  - Pinned to registry-current versions at execution time (research required re-verification)
  - Coverage thresholds set at 50/40/50/50 and left intentionally failing until plan 02-04 adds real tests
  - Added ClientApp/.gitignore to exclude generated coverage/ output
metrics:
  tasks: 3
  completed_date: 2026-04-15
---

# Phase 02 Plan 03: Vitest + RTL Foundation Summary

Set up Vitest with jsdom and React Testing Library in ClientApp, with coverage thresholds declared (50% line / 40% branch) and one passing smoke test proving the runner works end-to-end.

## What Was Built

### Dependencies installed (devDependencies)

Resolved via `npm view <pkg> version` at execution time per plan directive:

| Package                        | Version |
| ------------------------------ | ------- |
| vitest                         | 4.1.4   |
| @vitest/coverage-v8            | 4.1.4   |
| jsdom                          | 29.0.2  |
| @testing-library/react         | 16.3.2  |
| @testing-library/user-event    | 14.6.1  |
| @testing-library/jest-dom      | 6.9.1   |

MSW was intentionally NOT added (per research D3 — hand-rolled `vi.stubGlobal('fetch')` is the chosen pattern).

### Configuration

- **`vitest.config.ts`** — jsdom environment, `./vitest.setup.ts` setupFile, v8 coverage provider with reporters `text`, `html`, `cobertura`. Thresholds: lines 50, branches 40, functions 50, statements 50. Excludes: test files, `main.tsx`, `vite-env.d.ts`.
- **`vitest.setup.ts`** — single line: `import '@testing-library/jest-dom/vitest';`
- **`tsconfig.json`** — added `"types": ["vitest/globals", "@testing-library/jest-dom"]` and extended `include` to cover `vitest.config.ts` + `vitest.setup.ts`.
- **`package.json` scripts** — added `test`, `test:watch`, `test:coverage`.

### Smoke test

`src/smoke.test.ts` — 2 passing assertions:
1. `expect(1 + 1).toBe(2)`
2. `document.createElement('div')` is `HTMLElement` (proves jsdom is active)

## Verification

- `npm test` → exit 0, 2/2 tests passing (duration 774 ms)
- `npm run test:coverage` → runs, emits `coverage/` with cobertura XML + HTML. Threshold failure at 0% is expected (only smoke.test.ts exercises nothing) and will be resolved in plan 02-04 when real component/hook tests are added.
- `npx tsc --noEmit` → exit 0 (no type errors introduced)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Coverage output not gitignored**
- **Found during:** Task 3 (after running `test:coverage`)
- **Issue:** Plan produces `src/PassReset.Web/ClientApp/coverage/` every run; there was no `.gitignore` to exclude it — would pollute commits with generated HTML reports.
- **Fix:** Created `src/PassReset.Web/ClientApp/.gitignore` with `coverage/` entry.
- **Commit:** 63c8757

## Commits

| Task | Description                                                           | Commit   |
| ---- | --------------------------------------------------------------------- | -------- |
| 1    | `chore(02-03)`: add Vitest + RTL devDependencies                      | 577d0de  |
| 2    | `chore(02-03)`: add Vitest config, setup, tsconfig types, npm scripts | 8b07095  |
| 3    | `test(02-03)`: add Vitest smoke test proving runner + jsdom work      | 63c8757  |

## Known Stubs

None. The smoke test is intentionally trivial per plan — it exists to prove the runner works and will be joined (not replaced) by real tests in plan 02-04.

## Self-Check: PASSED

- FOUND: src/PassReset.Web/ClientApp/vitest.config.ts
- FOUND: src/PassReset.Web/ClientApp/vitest.setup.ts
- FOUND: src/PassReset.Web/ClientApp/src/smoke.test.ts
- FOUND: src/PassReset.Web/ClientApp/.gitignore
- FOUND commit: 577d0de
- FOUND commit: 8b07095
- FOUND commit: 63c8757
