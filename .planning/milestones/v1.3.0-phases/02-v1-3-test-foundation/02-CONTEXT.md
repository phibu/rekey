---
phase: 2
phase_name: v1.3 Test Foundation
created: 2026-04-15
mode: discuss (recommendations accepted en bloc)
status: ready-for-research
---

# Phase 2 Context — v1.3 Test Foundation

## Goal (from ROADMAP.md)

Automated test suites exist for backend and frontend, wired into CI as blocking gates.

**Requirements covered:** QA-001
**Target release:** v1.3.0 (parallel with Phase 3)
**Success criteria:**
1. `dotnet test src/PassReset.sln` exercises provider logic, error mapping, SIEM, lockout decorator
2. `npm test` (Vitest + RTL) covers components, hooks, utilities (levenshtein, password generator)
3. CI fails on any test failure; coverage thresholds declared and enforced
4. `release.yml` blocks tag-triggered publishes on test failure

## Carried-Forward Decisions (do not re-litigate)

- **Stack locked** (2026-04-14): React 19 / MUI 6 / ASP.NET Core 10
- **Targets:** `net10.0-windows` for `PassReset.PasswordProvider` and `PassReset.Web`; `net10.0` for `PassReset.Common`
- **CI runner:** `windows-latest` (no Linux/macOS support)
- **No breaking config changes** — pre-v1.3 `appsettings.Production.json` must keep working
- **Deploy path:** PowerShell installer (MSI rolled back); tests must not assume MSI

## Codebase Scout Findings

- `src/PassReset.Tests/` exists on disk but contains **only stale build artifacts** (no `.cs`, no `.csproj`, not in `src/PassReset.sln`). Effectively greenfield.
- `src/PassReset.Web/ClientApp/package.json` has **no test dependencies** today.
- `.github/workflows/ci.yml` runs build only, no tests.
- `.github/workflows/release.yml` publishes on tag with no test gate.

### Existing seams that need coverage
- `LockoutPasswordChangeProvider` (decorator over `IPasswordChangeProvider`, in-memory `ConcurrentDictionary` — pure logic, easy to test)
- `ApiErrorCode` mapping incl. `PasswordTooRecentlyChanged` (BUG-002 regression risk)
- `PwnedPasswordChecker` (HTTP — testable via `HttpClient` injection)
- `SiemService` (no I/O on hot path; syslog/email side-effects mockable via interfaces)
- `Levenshtein` (C# server-side and TS client-side mirror)
- `PasswordChangeProvider.ValidateGroups`, error-code helpers, model-binding edge cases
- `passwordGenerator` (crypto-secure, rejection sampling)
- `useSettings` hook, `PasswordForm` rendering / error-state mapping, `PasswordStrengthMeter`

### Hard-to-test surfaces (intentionally out of unit-test scope this phase)
- Live AD bind via `UserPrincipal`/`PrincipalContext` (sealed types)
- Live SMTP send via MailKit
- Live syslog UDP/TCP
- IIS hosting / installer behavior
- reCAPTCHA v3 verification HTTP

## Decisions (all per recommended defaults)

### D1 — Test project layout
Single **`PassReset.Tests`** xUnit project:
- Path: `src/PassReset.Tests/PassReset.Tests.csproj`
- TFM: `net10.0-windows` (must reference Windows-targeting assemblies)
- Added to `src/PassReset.sln`
- References: `PassReset.Common`, `PassReset.PasswordProvider`, `PassReset.Web`
- Folder layout mirrors source: `Common/`, `PasswordProvider/`, `Web/Controllers/`, `Web/Services/`, `Web/Models/`
- Stack: xUnit + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` + `coverlet.collector` (+ `Moq` or `NSubstitute` — researcher to pick whichever is more idiomatic for the codebase; default `NSubstitute` for terser syntax)
- Web tests use `Microsoft.AspNetCore.Mvc.Testing` `WebApplicationFactory` for controller-level integration tests with the in-process `DebugPasswordChangeProvider`

**Defer:** Splitting per source assembly. Revisit only if compile-time or test-discovery pain emerges.

### D2 — AD / external boundary strategy
**Strategy (a): Test only what's already abstracted.**
- In scope: `LockoutPasswordChangeProvider`, `ApiErrorCode` mapping, `PwnedPasswordChecker` (with mocked `HttpMessageHandler`), `SiemService` (with mocked `IEmailService` and a fake syslog sink injected behind an interface if needed), validators, models, controllers via `WebApplicationFactory` + `DebugPasswordChangeProvider`, Levenshtein, helpers.
- Out of scope this phase: real `S.DS.AM` calls, real SMTP, real syslog, real reCAPTCHA HTTP.
- The `IPasswordChangeProvider` abstraction already gives us decorator-level coverage; do not refactor `PasswordChangeProvider` internals just for testability in Phase 2.

**Deferred follow-up (capture as backlog idea):** Introduce thin internal wrappers around `UserPrincipal`/`PrincipalContext` so the real AD code path becomes unit-testable. Decide after observing actual bug velocity.

### D3 — Frontend test stack
- **Runner:** Vitest
- **DOM:** jsdom (more mature, well-trodden with RTL + MUI)
- **Library:** `@testing-library/react` + `@testing-library/user-event` + `@testing-library/jest-dom`
- **Network mocking:** hand-rolled `vi.fn()`/`vi.stubGlobal('fetch', ...)` — **no MSW** (avoid dependency weight; our API surface is two endpoints)
- **Snapshots:** none (brittle under MUI version bumps)
- **Setup file:** `src/PassReset.Web/ClientApp/vitest.setup.ts` configuring `@testing-library/jest-dom` matchers
- **Config:** `vitest.config.ts` (coexists with `vite.config.ts`); test files live alongside source as `*.test.ts(x)`
- **Scripts:** `npm test`, `npm run test:watch`, `npm run test:coverage`

### D4 — Coverage thresholds (SC #3)
- **Backend (Coverlet):** 55% line, 45% branch — global, project-wide
- **Frontend (`@vitest/coverage-v8`):** 50% line, 40% branch — global
- Thresholds enforced via build-failing flags (`/p:Threshold=...` for Coverlet; `coverage.thresholds` block in vitest config)
- **Ratchet policy:** thresholds may be raised (never lowered) in future phases as coverage organically grows; planner to add a one-line note in `CHANGELOG.md` whenever ratcheted

### D5 — CI gate placement
- New reusable workflow: `.github/workflows/tests.yml` — runs `dotnet test` and `npm test` sequentially in one `windows-latest` job, publishes coverage as workflow artifact
- `ci.yml` calls `tests.yml` after the build step (`uses: ./.github/workflows/tests.yml`)
- `release.yml` calls the same `tests.yml` **before** the publish step — release tag will not produce a zip if any test fails
- Sequential `build → test` (single job) on each workflow — cheaper minutes than parallel matrix for our scale

### D6 — Coverage tooling & reporting
- **Backend:** Coverlet (`coverlet.collector` package) producing Cobertura XML; threshold enforced inline via `dotnet test ... /p:CollectCoverage=true /p:Threshold=55 /p:ThresholdType=line`
- **Frontend:** `@vitest/coverage-v8` with thresholds in `vitest.config.ts`
- Both publish coverage XML/JSON as `actions/upload-artifact` outputs with 30-day retention
- **No PR comment bot, no Codecov upload** — keep the gate minimal; revisit if a reviewer asks for it

## Folded Todos
None — STATE.md only had stale "Plan Phase 1" entry which is obsolete.

## Deferred Ideas (for backlog, not this phase)
- Wrap `S.DS.AM` types behind thin internal interfaces so real AD code path becomes unit-testable
- Live integration tests against a containerized test AD (Samba)
- PR coverage comments / Codecov upload
- Mutation testing (Stryker.NET / StrykerJS)
- Splitting `PassReset.Tests` per source assembly

## Canonical References

- `src/PassReset.sln` — must add new test project here
- `.github/workflows/ci.yml` — extend with `tests.yml` call
- `.github/workflows/release.yml` — add `tests.yml` gate before publish
- `src/PassReset.Web/ClientApp/package.json` — extend scripts + devDeps
- `src/PassReset.Web/ClientApp/vite.config.ts` — Vitest config sibling
- `.planning/REQUIREMENTS.md` — QA-001 acceptance criteria
- `.planning/ROADMAP.md` § Phase 2 — success criteria authoritative
- `CLAUDE.md` § Build commands — must update with `dotnet test` and `npm test` after Phase 2 ships

## Open Questions for Researcher
- Pick `NSubstitute` vs `Moq` (default to NSubstitute unless idiomatic precedent says otherwise — neither exists in the repo today)
- Confirm `Microsoft.NET.Test.Sdk` + xunit versions compatible with `net10.0-windows`
- Check whether Coverlet's MSBuild integration plays well with `WebApplicationFactory` test projects on .NET 10
- Choose a fake-syslog approach if `SiemService` is hard to seam without a small interface extraction
