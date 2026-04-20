---
phase: 10
slug: operational-readiness
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-20
---

# Phase 10 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (backend) + Vitest (frontend) + manual UAT (installer) |
| **Config file** | `src/PassReset.sln` (backend) / `src/PassReset.Web/ClientApp/vitest.config.ts` (frontend) |
| **Quick run command** | `dotnet test src/PassReset.sln --filter FullyQualifiedName~HealthController` |
| **Full suite command** | `dotnet test src/PassReset.sln --configuration Release` followed by `cd src/PassReset.Web/ClientApp && npm test -- --run` |
| **Estimated runtime** | ~90 seconds (backend ~60s + frontend ~30s) |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test src/PassReset.sln --filter FullyQualifiedName~HealthController` (backend tasks) or `cd src/PassReset.Web/ClientApp && npm test -- --run AdPasswordPolicyPanel` (frontend tasks)
- **After every plan wave:** Run full suite (both `dotnet test` and `npm test -- --run`)
- **Before `/gsd-verify-work`:** Full suite must be green; manual installer UAT checklist signed off
- **Max feedback latency:** 90 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 10-01-01 | 01 | 1 | STAB-018 | — | Secrets never appear in /api/health body; error details only to SIEM | unit | `dotnet test --filter FullyQualifiedName~HealthControllerTests.Health_Body_ContainsNoSecrets` | ❌ W0 | ⬜ pending |
| 10-01-02 | 01 | 1 | STAB-018 | — | SMTP probe respects 3s timeout; no thread exhaustion under unresponsive host | unit | `dotnet test --filter FullyQualifiedName~SmtpProbeTests` | ❌ W0 | ⬜ pending |
| 10-01-03 | 01 | 1 | STAB-018 | — | Aggregate status: any unhealthy→unhealthy, any degraded→degraded, else healthy | unit | `dotnet test --filter FullyQualifiedName~HealthAggregationTests` | ❌ W0 | ⬜ pending |
| 10-01-04 | 01 | 1 | STAB-018 | — | ExpiryService "skipped" path returns healthy when Enabled=false | unit | `dotnet test --filter FullyQualifiedName~ExpiryServiceDiagnosticsTests` | ❌ W0 | ⬜ pending |
| 10-02-01 | 02 | 2 | STAB-019 | — | Installer hard-fails on 503 from /api/health after 10 retries | manual UAT | Run `Install-PassReset.ps1` against test VM with stopped AppPool; confirm exit≥1 | — | ⬜ pending |
| 10-02-02 | 02 | 2 | STAB-019 | — | `-SkipHealthCheck` bypasses post-deploy verification on air-gapped hosts | manual UAT | Run installer with flag; confirm no HTTP call attempted | — | ⬜ pending |
| 10-02-03 | 02 | 2 | STAB-019 | — | Success path prints ASCII-only summary (CI-friendly, no emoji in log output) | manual UAT | Pipe installer output through `Select-String -NotMatch '[\x{1F300}-\x{1FAFF}]'` | — | ⬜ pending |
| 10-03-01 | 03 | 3 | STAB-020 | — | CI fails on high/critical npm audit findings not in allowlist | integration | Push branch with known-vuln dep; confirm workflow fails | ❌ W0 | ⬜ pending |
| 10-03-02 | 03 | 3 | STAB-020 | — | CI fails on High/Critical dotnet vulnerable findings not in allowlist | integration | Push branch with known-vuln NuGet; confirm workflow fails | ❌ W0 | ⬜ pending |
| 10-03-03 | 03 | 3 | STAB-020 | — | Allowlist entries expire ≤90 days; expired entries fail CI | integration | Add entry with past expiration; confirm workflow fails | ❌ W0 | ⬜ pending |
| 10-04-01 | 04 | 4 | STAB-021 | — | Panel visible by default above username field without user interaction | unit | `cd src/PassReset.Web/ClientApp && npm test -- --run AdPasswordPolicyPanel` | ✅ (extends) | ⬜ pending |
| 10-04-02 | 04 | 4 | STAB-021 | — | Panel keyboard-navigable with correct role/aria attributes | unit | `cd src/PassReset.Web/ClientApp && npm test -- --run AdPasswordPolicyPanel.a11y` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `src/PassReset.Tests/Controllers/HealthControllerTests.cs` — extend existing file with new nested-checks assertions (STAB-018)
- [ ] `src/PassReset.Tests/Services/SmtpProbeTests.cs` — new file for TCP probe timeout behavior
- [ ] `src/PassReset.Tests/Services/ExpiryServiceDiagnosticsTests.cs` — new file for diagnostics interface contract
- [ ] `src/PassReset.Web/ClientApp/src/components/__tests__/AdPasswordPolicyPanel.test.tsx` — extend existing test with visibility-by-default assertion
- [ ] `src/PassReset.Web/ClientApp/src/components/__tests__/AdPasswordPolicyPanel.a11y.test.tsx` — new a11y test
- [ ] No installer test framework — Pester is explicitly out of scope (CONTEXT.md D-19, R-05). STAB-019 uses manual UAT checklist only.

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Installer post-deploy health check (success path) | STAB-019 | No Pester infra; adding one is out of scope per CONTEXT.md R-05 | 1. Install to clean Windows VM with AD domain-join. 2. Confirm ✓ Health OK summary printed. 3. Confirm exit code 0. |
| Installer post-deploy health check (failure path) | STAB-019 | Same — no Pester | 1. Stop AppPool before install completes. 2. Confirm 10×2s retry messages. 3. Confirm exit code ≥1 with response body printed. |
| Installer `-SkipHealthCheck` switch | STAB-019 | Same — no Pester | 1. Invoke installer with `-SkipHealthCheck`. 2. Confirm no HTTP request attempted (check IIS access log). |
| CI security-audit job end-to-end | STAB-020 | Requires real GitHub Actions run | 1. Open PR with seeded high-severity npm advisory. 2. Confirm workflow fails at security-audit job. 3. Add to allowlist. 4. Re-run, confirm workflow passes. |
| Allowlist expiration enforcement | STAB-020 | Requires date-mocked CI run | 1. Add allowlist entry with `expires: 2020-01-01`. 2. Push PR. 3. Confirm workflow fails with expired-advisory message. |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies or manual UAT justification
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify (STAB-019's 3 manual-only tasks are a documented exception — no automation possible without adding Pester)
- [ ] Wave 0 covers all MISSING references (5 test files listed above)
- [ ] No watch-mode flags in CI paths (`--run` used for Vitest, default for dotnet test)
- [ ] Feedback latency < 90s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
