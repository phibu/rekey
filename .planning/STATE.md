# PassReset — Project State

**Last updated:** 2026-04-14

## Project Reference

- **Project:** PassReset (self-service AD password change portal)
- **Core value:** Reliable, secure, self-service password change that fits corporate AD environments without bespoke deployment engineering
- **Baseline version:** v1.2.2
- **Current milestone:** v1.2.3 (Hotfix)
- **Milestone chain:** v1.2.3 → v1.3.0 → v2.0.0
- **Current focus:** Phase 1 — v1.2.3 Hotfix (3 P1 bugs)

## Current Position

- **Phase:** 1 — v1.2.3 Hotfix
- **Plan:** — (not yet planned; run `/gsd-plan-phase 1`)
- **Status:** Not started
- **Progress:** [░░░░░░░░░░] 0% (0/6 phases complete)

## Milestone Map

| Milestone | Phases | Status |
|---|---|---|
| v1.2.3 | 1 | Active |
| v1.3.0 | 2, 3 (parallel) | Pending |
| v2.0.0 | 4, 5, 6 | Pending |

## Performance Metrics

- Phases complete: 0/6
- Requirements delivered: 0/11
- Releases shipped (this chain): 0/3

## Accumulated Context

### Key Decisions

- **2026-04-13:** MSI packaging rolled back; PowerShell installer is the supported deployment path
- **2026-04-14:** v1.2.3 scoped as bugs-only hotfix; v1.3 runs QA-001 (tests) in parallel with UX features
- **2026-04-14:** Coarse granularity + parallel plan execution chosen for the three-milestone chain
- **2026-04-14:** Tech stack locked (React 19 / MUI 6 / ASP.NET Core 10) for v1.2.3 and v1.3.0; v2.0 may introduce cross-platform infrastructure

### Active TODOs

- Plan Phase 1 via `/gsd-plan-phase 1`

### Blockers

- None

### Notes

- `CLAUDE.md` still contains stale `<!-- GSD:project-start -->` markers referring to the rolled-back MSI v2.0 scope. Authoritative v2.0 scope lives in `.planning/PROJECT.md` and `.planning/REQUIREMENTS.md`.
- No automated tests exist yet (QA-001 in Phase 2 introduces them).

## Session Continuity

- **Previous session:** Project initialization (2026-04-14) — PROJECT.md, REQUIREMENTS.md, config.json created
- **This session:** Roadmap created (6 phases, 11/11 requirements mapped)
- **Next session:** Run `/gsd-plan-phase 1` to decompose v1.2.3 Hotfix into executable plans
