---
gsd_state_version: 1.0
milestone: v2.0
milestone_name: Multi-OS PoC
status: executing
last_updated: "2026-04-20T06:50:05.387Z"
progress:
  total_phases: 7
  completed_phases: 3
  total_plans: 21
  completed_plans: 18
  percent: 86
---

# PassReset — Project State

**Last updated:** 2026-04-17

## Project Reference

- **Project:** PassReset (self-service AD password change portal)
- **Core value:** Reliable, secure, self-service password change that fits corporate AD environments without bespoke deployment engineering
- **Baseline version:** v1.3.2
- **Current milestone:** v1.4.0 (Stabilization) — active
- **Queued milestone:** v2.0.0 (Platform evolution)
- **Milestone chain:** v1.2.3 ✅ → v1.3.0 ✅ → v1.3.1 ✅ → v1.3.2 ✅ → v1.4.0 (active) → v2.0.0 (queued)
- **Current focus:** v1.4.0 ready to ship — all 4 phases code-complete

## Current Position

Phase: 10 (operational-readiness) — CODE-COMPLETE (STAB-019 Task 3 operator UAT deferred)
Plans: 4 of 4 complete
Milestone: v1.4.0 — 4/4 phases complete (7 ✓, 8 ✓, 9 ✓, 10 ✓)
Next: operator UAT on Windows/IIS host for `deploy/HUMAN-UAT-10-02.md`, then tag v1.4.0

- **Phase:** 10 (code-complete, awaiting operator UAT)
- **Next:** Run `deploy/HUMAN-UAT-10-02.md` scenarios A–D on a real host OR defer + tag v1.4.0
- **Status:** Ready to ship modulo one deferred human-verify checkpoint
- **Progress:** [██████████] 100%

## Milestone Map

| Milestone | Phases | Status |
|---|---|---|
| v1.2.3 | 01 | ✅ Shipped 2026-04-14 (archived) |
| v1.3.0 | 02, 03 | ✅ Shipped 2026-04-15 (archived) |
| v1.3.1 | 07 (legacy) | ✅ Shipped 2026-04-15 (archived) |
| v1.3.2 | 07 (code review fix rollup) | ✅ Shipped 2026-04-16 (archived) |
| v1.4.0 | 7 ✓, 8 ✓, 9 ✓, 10 ✓ (UAT deferred) | Active — 4/4 phases code-complete |
| v2.0.0 | 11, 12, 13, 14 | Queued — 0/4 phases started |

> Note: legacy phase 07 numbering belongs to the archived v1.3.1/v1.3.2 milestones. The v1.4.0 chain restarts the active phase numbering at 7 going forward; archived directories are not affected.

## Performance Metrics

- Phases complete: 4/11 (01, 02, 03, legacy 07)
- Plans complete in shipped milestones: 13/13
- Requirements delivered: BUG-001..004, QA-001, FEAT-001..004 (9/33 across the full chain)
- Releases shipped: 4/6 (v1.2.3, v1.3.0, v1.3.1, v1.3.2)

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

### Active TODOs

- Phase 10-02 Task 3 operator UAT — run `deploy/HUMAN-UAT-10-02.md` scenarios A–D on Windows/IIS/AD host, or defer per Phase 7 precedent
- Phase 7 human UAT (5 operator items) — needs physical IIS host when available
- Phase 9 human-verify — operator review of 09-05 doc wording before v1.4.0 tag
- Phase 9 — 7 Info-level findings remain in 09-REVIEW.md (backlog)
- Triage Dependabot branches before cutting v1.4.0
- After v1.4.0 ships → `/gsd-discuss-phase 11` for v2.0 Multi-OS PoC

### Blockers

- None

### Notes

- Forensic report for 2026-04-15 partial-commit recovery: `.planning/forensics/report-20260415-122540.md`
- Backlog item 999.1 (E_ACCESSDENIED diagnosis) delivered in v1.3.1 (BUG-004); STAB-004 (gh#36) is a *new* E_ACCESSDENIED case on consecutive changes
- `CLAUDE.md` still contains stale `<!-- GSD:project-start -->` markers referring to the rolled-back MSI v2.0 scope. Authoritative scope lives in `.planning/PROJECT.md` and `.planning/REQUIREMENTS.md`
- v2.0 phase numbering changed: was 4/5/6, now 11/12/13 (sequential after v1.4.0 phases 7–10)

## Session Continuity

- **Previous session (2026-04-16, earlier):** Cut v1.3.2 patch rolling up post-v1.3.1 review fixes. Rolled STATE.md to v2.0.0 queued.
- **This session (2026-04-16):** 21 GitHub issues (#19–#39) opened against v1.3.2. Inserted v1.4.0 stabilization milestone before v2.0. Created STAB-001..021 requirements, phases 7–10, renumbered v2.0 phases 4/5/6 → 11/12/13.
- **Next session:** `/gsd-discuss-phase 7` to start Installer & Deployment Fixes. Phases 7/8/9 are parallelizable.
- **2026-04-16 (later):** Executed Phase 08-01 — pure-JSON production template, authoritative `appsettings.schema.json` (Draft 2020-12), csproj Content wiring. Commits: e81b839, fcd704b, b9deb9d. STAB-007 + STAB-008 ✓.
- **2026-04-17:** Executed Phase 09 (security-hardening) inline after a failed parallel-wave dispatch left 4 plans half-committed. Reconciled and finished all 5 plans sequentially. Full test suite: 164/164 green (up from 161). Commits landed for STAB-013 (account-enum collapse), STAB-014 (rate-limit + recaptcha tests), STAB-015 (AuditEvent + RFC5424 SD-ID), STAB-016 (HSTS IOptions resolution + tests), STAB-017 (env-var + user-secrets docs + regression test). CHANGELOG `[Unreleased]` now reflects STAB-013..017. Operator human-verify pending on 09-05 doc wording. See `tasks/lessons.md` 2026-04-17 entries for the parallel-execution fallout.
