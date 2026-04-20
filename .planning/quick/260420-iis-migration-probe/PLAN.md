---
quick_id: 260420-iis-migration-probe
slug: iis-migration-probe
date: 2026-04-20
status: in-progress-pending-operator
---

# Quick Task: IIS Migration Probe (pre-v1.4.3)

## Why

After shipping v1.4.2, `Install-PassReset.ps1` still fails on the operator's Windows Server host even with `IIS-ManagementScriptingTools` installed. Three speculative PS-7 fixes (4d61736, b549db3 → superseded, d560fb1, 44c8a1c) did not resolve the `Cannot find drive IIS` error. Before committing to a 2-3 hour migration to `IISAdministration`, gather concrete diagnostic data from the failing host.

## What

- Add `deploy/Test-PS7Iis.ps1` — read-only diagnostic probe that reports:
  - PS version / edition / OS build
  - Whether `WebAdministration` loads cleanly, exposes `IIS:\` drive locally
  - Whether `IISAdministration` loads natively (no WinPSCompat warning)
  - Whether `Get-IISAppPool`, `Get-IISSite`, `Get-IISServerManager` work
  - Whether a `WinPSCompatSession` is active
- Fix the contradictory PS 5.1 workaround text in the Install-PassReset.ps1 abort message. Installer has `#Requires -Version 7.0` — suggesting to run under 5.1 is self-refuting.

## Commit

`ae76019` chore(deploy): add Test-PS7Iis.ps1 diagnostic + fix abort text

## Next (pending operator)

1. Operator runs `pwsh -NoProfile -File .\Test-PS7Iis.ps1` on the failing host.
2. Operator pastes output to session or GitHub issue.
3. If `IISAdministration` native import succeeds → route to `/gsd-insert-phase` for the migration (Phase 10.5 or equivalent, ships as v1.4.3).
4. If `IISAdministration` also routes through WinPSCompat → pivot to an `Invoke-Command`-based approach or a child `powershell.exe` shell-out for the IIS blocks.
