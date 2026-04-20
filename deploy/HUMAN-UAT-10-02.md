# HUMAN-UAT 10-02 — Installer Post-Deploy Verification

**Requirement:** STAB-019
**Phase:** [10-operational-readiness](../.planning/phases/10-operational-readiness/)
**Target:** `deploy/Install-PassReset.ps1` (post-deploy verification block)
**Run on:** clean Windows Server VM with IIS + AD domain-join

This checklist exercises the four operator-facing paths of the STAB-019
post-deploy verification block added in plan 10-02. The block calls
`GET /api/health` + `GET /api/password` after the IIS AppPool recycle,
retries 10x at 2s intervals, and hard-fails with `exit 1` if the app
never responds. The `-SkipHealthCheck` switch bypasses verification
entirely for air-gapped installs.

Automated Pester coverage for `Install-PassReset.ps1` is explicitly
**out of scope** per Phase 10 CONTEXT.md R-05 (manual UAT is the
canonical verification for installer regressions; the installer cannot
be meaningfully unit-tested without a real IIS + AD host).

---

## Prerequisites

- Windows Server 2019 or later VM (domain-joined to a test AD forest)
- IIS 10.0+ with ASP.NET Core 10.0 Hosting Bundle installed
- .NET 10 SDK (only needed if rebuilding locally)
- Administrator shell (the script declares `#Requires -RunAsAdministrator`)
- A fresh release zip built via:
  ```powershell
  .\deploy\Publish-PassReset.ps1 -Version vTEST
  ```
- Optional: a valid HTTPS certificate thumbprint in `LocalMachine\My`
  for the HTTPS variant (not required for this UAT — HTTP-only binding
  exercises the same verification path).

---

## Scenario A — Success path (verification passes)

**Goal:** confirm `Health OK -- AD: ..., SMTP: ..., ExpiryService: ...`
is printed and the script exits 0.

### Steps

1. Extract the release zip onto the target VM.
2. Open an elevated PowerShell 7+ shell in the extracted folder.
3. Run the installer interactively with minimal parameters:
   ```powershell
   .\Install-PassReset.ps1
   ```
4. Accept any upgrade prompts if an existing install is detected.
5. Wait for the installer to finish its IIS provisioning, config write,
   AppPool recycle, and URL announcement block.
6. Observe the "Verifying deployment at http://..." step and the
   subsequent `Health OK --` line.

### Expected results

- [ ] `[>>] Verifying deployment at http://<hostname>:<port> (up to 10 x 2s)` printed.
- [ ] `[OK] Health OK -- AD: healthy, SMTP: <healthy|skipped>, ExpiryService: <healthy|not-enabled>` printed.
- [ ] Exit code: `0` — confirm with `$LASTEXITCODE` immediately after the run.
- [ ] No `Post-deploy health check failed` line anywhere in the output.

### Operator records

| Field                 | Value |
| --------------------- | ----- |
| AD status value       |       |
| SMTP status value     |       |
| ExpiryService status  |       |
| `$LASTEXITCODE`       |       |

---

## Scenario B — Failure path (AppPool stopped mid-verify)

**Goal:** confirm the script retries 10 times, prints the last
response body, and hard-fails with exit code >= 1.

### Steps

1. Begin a fresh install as in Scenario A, but do NOT dismiss the
   shell yet — open a second elevated PowerShell window.
2. Start the installer in window 1:
   ```powershell
   .\Install-PassReset.ps1
   ```
3. When the installer reaches `[>>] Verifying deployment at ...`,
   immediately run in window 2:
   ```powershell
   Stop-WebAppPool -Name PassResetPool
   ```
   (Adjust the pool name if you used a custom `-AppPoolName`.)
4. Let the installer continue retrying without intervention.

### Expected results

- [ ] Ten `WARNING: Attempt N/10: <message>` lines spaced ~2s apart.
- [ ] Final line matches the installer's `Write-Error` format verbatim:
      `Post-deploy health check failed after 10 attempts. Last /api/health response: ...`
- [ ] Exit code: `>= 1` — confirm with `$LASTEXITCODE`.
- [ ] No `Health OK --` line was ever printed.

### Operator records

| Field                              | Value |
| ---------------------------------- | ----- |
| Number of retry warnings observed  |       |
| Final error message (first 120 ch) |       |
| `$LASTEXITCODE`                    |       |

---

## Scenario C — `-SkipHealthCheck` bypass

**Goal:** confirm the verification block is skipped and no HTTP
requests hit `/api/health` or `/api/password` during the run.

### Steps

1. Clear the IIS access log or note the current max timestamp:
   ```powershell
   Get-ChildItem "$env:SystemDrive\inetpub\logs\LogFiles\W3SVC*\u_ex*.log" |
     Sort-Object LastWriteTime -Descending | Select-Object -First 1
   ```
2. Run the installer with `-SkipHealthCheck`:
   ```powershell
   .\Install-PassReset.ps1 -SkipHealthCheck
   ```
3. After the installer exits, inspect the IIS access log for entries
   matching `/api/health` or `/api/password` whose timestamp is after
   the run start time.

### Expected results

- [ ] `[>>] Skipping post-deploy health check (-SkipHealthCheck specified)` printed.
- [ ] No `Verifying deployment at ...` line.
- [ ] No `Health OK --` line.
- [ ] IIS access log contains **zero** `/api/health` or `/api/password`
      hits dated after the installer start time.
- [ ] Exit code: `0`.

### Operator records

| Field                                             | Value |
| ------------------------------------------------- | ----- |
| Skip message printed (yes/no)                     |       |
| `/api/health` hits in access log during run       |       |
| `/api/password` hits in access log during run     |       |
| `$LASTEXITCODE`                                   |       |

---

## Scenario D — `-Force` mode still verifies (no interactive prompts)

**Goal:** confirm the verification block runs under `-Force` and does
NOT prompt (D-06/D-07). Failure under `-Force` must still hard-fail.

### Steps

1. Ensure an existing PassReset install is present (required to
   exercise the upgrade path where `-Force` matters).
2. Run the installer with `-Force` (no `-SkipHealthCheck`):
   ```powershell
   .\Install-PassReset.ps1 -Force
   ```
3. Observe that no confirmation prompts appear.
4. Confirm the `[>>] Verifying deployment at ...` line is printed
   exactly as in Scenario A.
5. Optional stress check: repeat step 2 with the AppPool pre-stopped
   (`Stop-WebAppPool -Name PassResetPool` before invoking) — confirm
   the installer still retries 10 times and hard-fails without
   prompting the operator.

### Expected results

- [ ] No `[Y/N]` prompts during the run.
- [ ] `Verifying deployment at ...` line printed (verification ran).
- [ ] Either `Health OK --` (success) or the 10-retry `Post-deploy
      health check failed ...` hard-fail — never silently skipped.
- [ ] `-Force` did NOT bypass verification.

### Operator records

| Field                                    | Value |
| ---------------------------------------- | ----- |
| Prompts observed during run (yes/no)     |       |
| Verification ran (yes/no)                |       |
| Final outcome (success / hard-fail)      |       |
| `$LASTEXITCODE`                          |       |

---

## Sign-off

| Field          | Value |
| -------------- | ----- |
| Operator name  |       |
| Date (UTC)     |       |
| VM hostname    |       |
| AD domain      |       |
| Build / tag    |       |

### Scenario outcomes

| Scenario                          | Pass / Fail / Deferred | Notes |
| --------------------------------- | ---------------------- | ----- |
| A — Success path                  |                        |       |
| B — Failure path (AppPool stop)   |                        |       |
| C — `-SkipHealthCheck` bypass     |                        |       |
| D — `-Force` still verifies       |                        |       |

**Deferral note (optional):** If no VM is available, sign off as
`DEFERRED — physical host unavailable` and record the reason here.
This mirrors the Phase 7 operator UAT deferral pattern documented in
`.planning/STATE.md`.

**Out-of-scope note:** Pester-based automation for `Install-PassReset.ps1`
is explicitly out of scope per Phase 10 CONTEXT.md R-05. This checklist
is the canonical verification for STAB-019.
