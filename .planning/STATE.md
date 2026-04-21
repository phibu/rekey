---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: Platform evolution
status: executing
last_updated: "2026-04-21T15:35:00.000Z"
progress:
  total_phases: 8
  completed_phases: 5
  total_plans: 43
  completed_plans: 40
  percent: 93
---

# PassReset — Project State

**Last updated:** 2026-04-21

## Project Reference

- **Project:** PassReset (self-service AD password change portal)
- **Core value:** Reliable, secure, self-service password change that fits corporate AD environments without bespoke deployment engineering
- **Baseline version:** v1.4.0
- **Current milestone:** v2.0.0 (Platform evolution) — active
- **Milestone chain:** v1.2.3 ✅ → v1.3.0 ✅ → v1.3.1 ✅ → v1.3.2 ✅ → v1.4.0 ✅ → v2.0.0 (active)
- **Current focus:** v2.0 Phase 11 shipped as 2.0.0-alpha.1 (cross-platform LDAP provider); Phase 12 up next

## Current Position

Phase: 11 (v2.0 Multi-OS PoC) — ✅ SHIPPED as 2.0.0-alpha.1 via PR #41 (merge `8fd787f`) on 2026-04-21
Plans: 22 of 22 complete (managed via superpowers plan, not gsd phase dir)
Milestone: v2.0.0 — 1/4 phases complete (11 ✓, 12 ⏭, 13 ⏭, 14 ⏭)
Next: `/gsd-discuss-phase 12` for v2.0 Local Password DB

- **Phase:** 12 (v2.0 Local Password DB) — not yet started
- **Next:** Discuss Phase 12 scope (operator-managed banned-words + attempted-pwned lookup store)
- **Status:** Phase 11 shipped cleanly; 232 passing + 8 skipped tests; Samba DC integration test green
- **Progress:** v2.0 [██▌░░░░░░░] 25% (1/4 phases)

## Milestone Map

| Milestone | Phases | Status |
|---|---|---|
| v1.2.3 | 01 | ✅ Shipped 2026-04-14 (archived) |
| v1.3.0 | 02, 03 | ✅ Shipped 2026-04-15 (archived) |
| v1.3.1 | 07 (legacy) | ✅ Shipped 2026-04-15 (archived) |
| v1.3.2 | 07 (code review fix rollup) | ✅ Shipped 2026-04-16 (archived) |
| v1.4.0 | 7 ✓, 8 ✓, 9 ✓, 10 ✓ (UAT deferred) | ✅ Code-complete |
| v2.0.0 | 11 ✓, 12, 13, 14 | Active — 1/4 phases complete (Phase 11 alpha.1 shipped 2026-04-21) |

> Note: legacy phase 07 numbering belongs to the archived v1.3.1/v1.3.2 milestones. The v1.4.0 chain restarts the active phase numbering at 7 going forward; archived directories are not affected.

## Performance Metrics

- Phases complete: 5/11 (01, 02, 03, legacy 07, 11)
- Plans complete in shipped milestones: 35/35 (13 prior + 22 Phase 11)
- Requirements delivered: BUG-001..004, QA-001, FEAT-001..004, V2-001 (10/33)
- Releases shipped: 5/6 (v1.2.3, v1.3.0, v1.3.1, v1.3.2, 2.0.0-alpha.1)

## Accumulated Context

### Key Decisions

- **2026-04-13:** MSI packaging rolled back; PowerShell installer is the supported deployment path
- **2026-04-14:** v1.2.3 scoped as bugs-only hotfix; v1.3 runs QA-001 (tests) in parallel with UX features
- **2026-04-14:** Coarse granularity + parallel plan execution chosen for the milestone chain
- **2026-04-14:** Tech stack locked (React 19 / MUI 6 / ASP.NET Core 10); v2.0 may introduce cross-platform infrastructure
- **2026-04-15:** Phase 03-02 split across two sessions; client half recovered via forensics and committed as 133a2a4
- **2026-04-16:** v1.3.2 cut as a code-review-fix patch rollup on top of v1.3.1 (WR-01/WR-02/WR-03); no new phase created
- **2026-04-16:** Insert v1.4.0 stabilization milestone before v2.0 — 21 GitHub issues opened against v1.3.2 represent install/security regressions
- **2026-04-16:** STAB-017 (env-var secrets) is a stepping stone, not the full V2-003 — env vars unblock production now without committing to the v2.0 DPAPI/Key Vault decision
- **2026-04-16 (Phase 08-01):** appsettings.schema.json uses a live `x-passreset-obsolete` marker on a legacy Recaptcha key (rather than `$comment`-only convention) so the installer sync code in 08-05/06 has an executable test case for the obsolete-key prompt path
- **2026-04-16 (Phase 08-01):** csproj uses `<Content Update>` (not `<Content Include>`) to ship the schema + template; ASP.NET Core Web SDK auto-includes JSON as Content, so `Include` triggers `NETSDK1022` duplicate-item errors
- **2026-04-20 (Phase 11 plan):** Phase 11 executed via superpowers subagent-driven-development (not gsd phase dir) per plan at `docs/superpowers/plans/2026-04-20-phase-11-ldap-provider.md`; artifacts diverge from the `.planning/phases/11-*/` convention used by other v2.0 phases
- **2026-04-21 (Phase 11 ship):** `PasswordChangeOptions` relocated from `PassReset.PasswordProvider` (Windows) to `PassReset.Common` (platform-neutral); `ProviderMode` enum (Auto/Windows/Ldap, default Auto) added for runtime selection; Windows provider preserved byte-for-byte; conditional TFM used on `PassReset.Web` + `PassReset.Tests` (net10.0-windows on Windows, net10.0 elsewhere) because NU1201 blocks pure net10.0 referencing net10.0-windows projects
- **2026-04-21 (Phase 11 known gap):** `HealthController.cs` still references `PrincipalContext` unguarded; Linux deployment blocked until a follow-up phase refactors this. Documented in CHANGELOG `[2.0.0-alpha.1] — Known Limitations`
- **2026-04-21 (CI):** Reusable workflows (`uses: ./.github/workflows/tests.yml`) require explicit `secrets: inherit` from caller; otherwise `secrets.X` evaluates empty inside the callee. Critical fix landed in Phase 11 and applies to any future reusable-workflow callers

### Active TODOs

- Phase 10-02 Task 3 operator UAT — run `deploy/HUMAN-UAT-10-02.md` scenarios A–D on Windows/IIS/AD host, or defer per Phase 7 precedent
- Phase 7 human UAT (5 operator items) — needs physical IIS host when available
- Phase 9 human-verify — operator review of 09-05 doc wording before v1.4.0 tag
- Phase 9 — 7 Info-level findings remain in 09-REVIEW.md (backlog)
- Phase 11 follow-up — guard or refactor `HealthController.PrincipalContext` usage so Linux host can build (prerequisite for promoting alpha.1 → beta)
- Phase 11 follow-up — expand Samba integration smoke test beyond the single happy-path scenario (cover InvalidCredentials / ChangeNotPermitted / min-pwd-age) before v2.0 GA
- Phase 11 follow-up — the 7 Windows contract tests in `PassReset.Tests.Windows/Contracts/` are `[Fact(Skip=...)]` pending an `IPrincipalContext` seam on the Windows provider
- Phase 12 kickoff — `/gsd-discuss-phase 12` for v2.0 Local Password DB (operator-managed banned-words + attempted-pwned lookup store)

### Blockers

- None

### Notes

- Forensic report for 2026-04-15 partial-commit recovery: `.planning/forensics/report-20260415-122540.md`
- Backlog item 999.1 (E_ACCESSDENIED diagnosis) delivered in v1.3.1 (BUG-004); STAB-004 (gh#36) is a *new* E_ACCESSDENIED case on consecutive changes
- `CLAUDE.md` still contains stale `<!-- GSD:project-start -->` markers referring to the rolled-back MSI v2.0 scope. Authoritative scope lives in `.planning/PROJECT.md` and `.planning/REQUIREMENTS.md`
- v2.0 phase numbering changed: was 4/5/6, now 11/12/13 (sequential after v1.4.0 phases 7–10); Phase 14 (Web Admin UI) added later
- Phase 11 artifacts live at `docs/superpowers/plans/2026-04-20-phase-11-ldap-provider.md` + `docs/superpowers/specs/2026-04-20-phase-11-ldap-provider.md`, NOT in `.planning/phases/11-*/`. Future v2.0 phases (12/13/14) will use the standard `.planning/phases/` layout unless explicitly noted otherwise.
- Commit a259a73 in Phase 11 refactored STATE snapshot-semantics and LdapHostnames guard in Program.cs; the guard is defense-in-depth above `PasswordChangeOptionsValidator`

## Session Continuity

- **Previous session (2026-04-16, earlier):** Cut v1.3.2 patch rolling up post-v1.3.1 review fixes. Rolled STATE.md to v2.0.0 queued.
- **This session (2026-04-16):** 21 GitHub issues (#19–#39) opened against v1.3.2. Inserted v1.4.0 stabilization milestone before v2.0. Created STAB-001..021 requirements, phases 7–10, renumbered v2.0 phases 4/5/6 → 11/12/13.
- **Next session:** `/gsd-discuss-phase 7` to start Installer & Deployment Fixes. Phases 7/8/9 are parallelizable.
- **2026-04-16 (later):** Executed Phase 08-01 — pure-JSON production template, authoritative `appsettings.schema.json` (Draft 2020-12), csproj Content wiring. Commits: e81b839, fcd704b, b9deb9d. STAB-007 + STAB-008 ✓.
- **2026-04-17:** Executed Phase 09 (security-hardening) inline after a failed parallel-wave dispatch left 4 plans half-committed. Reconciled and finished all 5 plans sequentially. Full test suite: 164/164 green (up from 161). Commits landed for STAB-013 (account-enum collapse), STAB-014 (rate-limit + recaptcha tests), STAB-015 (AuditEvent + RFC5424 SD-ID), STAB-016 (HSTS IOptions resolution + tests), STAB-017 (env-var + user-secrets docs + regression test). CHANGELOG `[Unreleased]` now reflects STAB-013..017. Operator human-verify pending on 09-05 doc wording. See `tasks/lessons.md` 2026-04-17 entries for the parallel-execution fallout.
- **2026-04-21 (this session):** Shipped Phase 11 (v2.0 Multi-OS PoC) as 2.0.0-alpha.1 via PR #41 → master merge `8fd787f`. 22 tasks / 35 commits via superpowers subagent-driven-development (implementer + 2-stage reviewer loop per task). Delivers cross-platform `LdapPasswordChangeProvider` (net10.0) + `ProviderMode` selection + retargeted test projects + Samba AD DC CI integration test. 232 passing + 8 skipped tests; CodeQL clean after `.ToString()` on enum values broke the `cs/cleartext-storage` taint path. Significant CI debugging iterations: retired `diginc/samba-ad-dc:latest` → `nowsci/samba-domain:latest`, realm/NetBIOS mismatch, missing `secrets: inherit` in reusable-workflow caller, `samba-tool user create` positional-arg syntax, `LdapHostnames[0]` env-var binding. All root-caused and committed. Windows provider unchanged (V2-001 "non-breaking upgrade" preserved). Seventeenth stop-failure lesson added to `tasks/lessons.md`.
- **Next session:** `/gsd-discuss-phase 12` to start v2.0 Local Password DB. Phase 13 (Secure Config Storage) and Phase 14 (Web Admin UI) follow.
