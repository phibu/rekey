# PassReset — Roadmap

**Milestone chain:** v1.2.3 → v1.3.0 → v2.0.0
**Granularity:** coarse
**Parallelization:** enabled
**Created:** 2026-04-14
**Baseline:** v1.2.2

## Phases

- [ ] **Phase 1: v1.2.3 Hotfix** — Ship 3 P1 bug fixes as an atomic hotfix release
- [ ] **Phase 2: v1.3 Test Foundation** — Establish xUnit + Vitest test infrastructure with CI gates (parallel with Phase 3)
- [ ] **Phase 3: v1.3 UX Features** — Deliver branding, AD policy display, clipboard protection, HIBP blur indicator (parallel with Phase 2)
- [ ] **Phase 4: v2.0 Multi-OS PoC** — Research cross-platform path and produce Docker PoC
- [ ] **Phase 5: v2.0 Local Password DB** — Operator-managed banned-words + attempted-pwned lookup store
- [ ] **Phase 6: v2.0 Secure Config Storage** — Eliminate cleartext secrets from appsettings.Production.json

## Phase Details

### Phase 1: v1.2.3 Hotfix
**Goal**: Release v1.2.3 with all three P1 bugs fixed, no regressions to existing deployments
**Depends on**: Nothing (starts from v1.2.2 baseline)
**Parallel with**: None (sequential — release gate)
**Target release**: v1.2.3
**Requirements**: BUG-001, BUG-002, BUG-003
**Success Criteria** (what must be TRUE):
  1. Operators can configure SMTP to trust an internal-CA-issued relay cert via documented, explicit trust config (thumbprint allowlist or CA-trust option) — no silent validation bypass
  2. A user who retries a password change within the domain `minPwdAge` window sees a dedicated "password changed too recently" message (not "Unexpected Error"), and a matching SIEM event is emitted
  3. Running `Install-PassReset.ps1` as an upgrade preserves the existing IIS AppPool identity (custom service account is not reset to `ApplicationPoolIdentity`)
  4. v1.2.3 tag produces a released zip via `release.yml` with `CHANGELOG.md`, `UPGRADING.md`, and `docs/appsettings-Production.md` updated
**Plans**: 3 plans
- [ ] 01-01-PLAN.md — BUG-001 SMTP internal-CA trust via thumbprint allowlist
- [ ] 01-02-PLAN.md — BUG-002 Map E_ACCESSDENIED to PasswordTooRecentlyChanged
- [ ] 01-03-PLAN.md — BUG-003 Preserve IIS AppPool identity on upgrade

### Phase 2: v1.3 Test Foundation
**Goal**: Automated test suites exist for backend and frontend, wired into CI as blocking gates
**Depends on**: Phase 1 (builds on v1.2.3 codebase)
**Parallel with**: Phase 3 (independent tracks inside v1.3 milestone)
**Target release**: v1.3.0 (shipped together with Phase 3)
**Requirements**: QA-001
**Success Criteria** (what must be TRUE):
  1. `dotnet test src/PassReset.sln` runs on a clean checkout and exercises provider logic, error mapping, SIEM service, and the lockout decorator
  2. `npm test` (Vitest + RTL) runs in `ClientApp/` and covers components, hooks, and utilities (levenshtein, password generator)
  3. CI workflow fails the build when any test fails; coverage thresholds are declared and enforced
  4. Release workflow (`release.yml`) blocks tag-triggered publishes on test failure
**Plans**: 5 plans
- [x] 02-01-PLAN.md — Backend test project scaffolding (xUnit v3, coverlet.msbuild, Program.cs partial)
- [ ] 02-02-PLAN.md — Backend test suite + PwnedPasswordChecker/SiemSyslogFormatter refactors + 55/45 thresholds
- [x] 02-03-PLAN.md — Frontend Vitest + RTL + jsdom infrastructure with 50/40 thresholds
- [x] 02-04-PLAN.md — Frontend test suites (components, hook, utilities) meeting coverage thresholds
- [ ] 02-05-PLAN.md — CI gate wiring (tests.yml, release.yml needs: tests, docs + CHANGELOG)

### Phase 3: v1.3 UX Features
**Goal**: Operators can brand the portal and users get clearer guidance and safer password UX
**Depends on**: Phase 1 (builds on v1.2.3 codebase)
**Parallel with**: Phase 2 (independent tracks inside v1.3 milestone)
**Target release**: v1.3.0 (shipped together with Phase 2)
**Requirements**: FEAT-001, FEAT-002, FEAT-003, FEAT-004
**Success Criteria** (what must be TRUE):
  1. An operator can set company name, portal name, helpdesk URL/email, usage text, logo, and favicon via `appsettings.Production.json` → `ClientSettings`, served from an upgrade-safe path, with defaults preserving the current look
  2. When `ShowAdPasswordPolicy=true`, users see effective AD minimum policy requirements above the new-password field; panel hides cleanly on AD query failure
  3. After using the password generator, the clipboard is cleared after `ClipboardClearSeconds` only if the clipboard still contains the generated password
  4. On new-password blur, users see a HIBP breach indicator (safe / breached + count), debounced, honoring `FailOpenOnPwnedCheckUnavailable`, with plaintext never leaving the client
  5. No breaking changes to existing `appsettings.Production.json` — pre-v1.3 configs continue to work
**Plans**: TBD
**UI hint**: yes

### Phase 4: v2.0 Multi-OS PoC
**Goal**: A documented, evidence-backed decision on cross-platform viability, validated by a working Docker PoC against a test AD
**Depends on**: Phase 2 (test foundation) + Phase 3 (v1.3 shipped)
**Parallel with**: None (findings gate Phases 5 and 6 design)
**Target release**: v2.0.0 (research deliverable; production migration may be deferred)
**Requirements**: V2-001
**Success Criteria** (what must be TRUE):
  1. A research document exists comparing `Novell.Directory.Ldap.NETStandard` (and alternatives) against the current `System.DirectoryServices.AccountManagement` usage, with a recommended path
  2. A Docker image builds from the repo and performs a successful password change against a test AD without `S.DS.AM`
  3. An explicit go/no-go decision on full Linux support is captured in `PROJECT.md` Key Decisions
  4. A provider abstraction boundary is identified (or confirmed sufficient) such that future cross-platform work doesn't require a rewrite
**Plans**: TBD

### Phase 5: v2.0 Local Password DB
**Goal**: Operators can enforce banned-word and attempted-pwned rules locally, independent of (and stricter than) AD policy
**Depends on**: Phase 4 (provider-abstraction findings inform integration shape)
**Parallel with**: Phase 6 (could overlap once Phase 4 lands, but coarse granularity keeps them sequential by default)
**Target release**: v2.0.0
**Requirements**: V2-002
**Success Criteria** (what must be TRUE):
  1. Operators can add and remove banned terms via a documented mechanism; changes take effect without code rebuild
  2. A local attempted-pwned lookup store exists and is consulted during password change; matches reject the change with a distinct `ApiErrorCode`
  3. Local rules are enforced even when AD would accept the password (strictly additive)
  4. Any borrowed logic (e.g., from lithnet/ad-password-protection) has a LICENSE-compatible integration documented in the repo
**Plans**: TBD

### Phase 6: v2.0 Secure Config Storage
**Goal**: Secrets in `appsettings.Production.json` are never stored as cleartext on disk by default, with a clear upgrade path for existing installs
**Depends on**: Phase 4 (cross-platform constraints shape mechanism choice — e.g., DPAPI is Windows-only)
**Parallel with**: Phase 5 (independent of V2-002 scope)
**Target release**: v2.0.0
**Requirements**: V2-003
**Success Criteria** (what must be TRUE):
  1. SMTP, reCAPTCHA, and LDAP credentials can be stored via a secure mechanism (DPAPI / ASP.NET Core Data Protection / Credential Manager / optional Key Vault adapter) chosen and documented
  2. A fresh install has no cleartext secrets on disk by default
  3. An existing v1.3.x install can upgrade to v2.0 and migrate its secrets following a documented procedure in `UPGRADING.md`
  4. `docs/Secret-Management.md` reflects the new default and documents fallback/override knobs
**Plans**: TBD

## Cross-Phase Dependencies

| From | To | Nature |
|---|---|---|
| Phase 1 | Phase 2 | v1.3 test foundation builds on v1.2.3 codebase |
| Phase 1 | Phase 3 | v1.3 features built on v1.2.3 codebase |
| Phase 2 ↔ Phase 3 | — | **Parallel**: same v1.3.0 milestone, independent tracks; both must complete before v1.3.0 tag |
| Phase 3 | Phase 4 | v2.0 work starts after v1.3.0 ships |
| Phase 4 | Phase 5 | Provider-abstraction decision informs local-DB integration point |
| Phase 4 | Phase 6 | Platform decision (Windows-only vs cross-platform) constrains secret-storage mechanism |

## Parallelism Map

- **Parallel pair:** Phase 2 (Test Foundation) + Phase 3 (UX Features) — both ship together as v1.3.0
- **Sequential:** Phase 1 → {Phase 2 ∥ Phase 3} → Phase 4 → Phase 5 → Phase 6
- Phases 5 and 6 *could* run in parallel once Phase 4 lands; coarse granularity keeps them sequential unless capacity allows.

## Progress

| Phase | Plans Complete | Status | Completed |
|---|---|---|---|
| 1. v1.2.3 Hotfix | 0/0 | Not started | — |
| 2. v1.3 Test Foundation | 0/0 | Not started | — |
| 3. v1.3 UX Features | 0/0 | Not started | — |
| 4. v2.0 Multi-OS PoC | 0/0 | Not started | — |
| 5. v2.0 Local Password DB | 0/0 | Not started | — |
| 6. v2.0 Secure Config Storage | 0/0 | Not started | — |

## Coverage

- Total v1 requirements: **11**
- Mapped: **11/11** ✓
- Orphans: **0**

---

## Backlog

### Phase 999.1: Diagnose E_ACCESSDENIED password change failures with structured logging (BACKLOG)

**Goal:** [Captured for future planning] Diagnose intermittent `0x80070005 (E_ACCESSDENIED)` password change failures by adding structured logging around every step of the AD password change flow. External behavior unchanged — only internal diagnostics improved.

**Requirements:** TBD

**Plans:** 0 plans

Plans:
- [ ] TBD (promote with /gsd:review-backlog when ready)

**Context captured:**
- Files: `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` (primary), `LockoutPasswordChangeProvider.cs`, `src/PassReset.Web/Controllers/PasswordController.cs`
- Add exception chain logger (type, HResult, message, depth)
- Step-before/after logging around user lookup, ChangePasswordInternal, Save()
- Targeted catches: `PasswordException`, `PrincipalOperationException`, `DirectoryServicesCOMException`
- Controller-level TraceId correlation via `HttpContext.TraceIdentifier`
- AD context logging (domain, DC, identity type, UserCannotChangePassword, LastPasswordSet)
- Lockout decorator state-transition logging
- Constraints: no passwords/secrets logged, user-facing errors remain generic, no DB/audit dependencies

---
*Last updated: 2026-04-15 (added backlog 999.1)*
