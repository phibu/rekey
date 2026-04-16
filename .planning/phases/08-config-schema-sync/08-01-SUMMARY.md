---
phase: 08-config-schema-sync
plan: 01
subsystem: config-schema
tags: [config, schema, json-schema, installer-foundation, stabilization]
requirements: [STAB-007, STAB-008]
dependency_graph:
  requires: []
  provides:
    - appsettings.schema.json (JSON Schema Draft 2020-12 manifest)
    - pure-JSON appsettings.Production.template.json
    - csproj Content-include wiring for both artifacts
  affects:
    - 08-02 (CI Test-Json validation step)
    - 08-03 (runtime IValidateOptions validators)
    - 08-04..06 (installer pre-flight, ConfigSync, additive-merge)
    - 08-07 (Publish-PassReset schema packaging)
    - 08-08 (operator docs)
tech_stack:
  added:
    - JSON Schema Draft 2020-12 (schema manifest format)
  patterns:
    - "<Content Update=...>" (not Include) to override Web SDK auto-included items
    - Custom x-passreset-obsolete / x-passreset-obsolete-since extensions (D-11)
key_files:
  created:
    - src/PassReset.Web/appsettings.schema.json
  modified:
    - src/PassReset.Web/appsettings.Production.template.json
    - src/PassReset.Web/PassReset.Web.csproj
decisions:
  - Real obsolete-key marker in use (not $comment-only convention): Recaptcha legacy key flagged with x-passreset-obsolete-since 1.3.0
  - Serilog section kept loose (additionalProperties: true, no required) — Serilog config is open-ended and not the focus of this schema
  - csproj uses Content Update (not Include) because ASP.NET Core Web SDK auto-includes JSON files as Content; Include triggers NETSDK1022 duplicate-item error
metrics:
  duration_minutes: ~60 (across two executor sessions)
  completed: "2026-04-16"
  tasks: 3
  files_touched: 3
---

# Phase 8 Plan 1: Config Schema Foundation Summary

Pure-JSON production template plus authoritative JSON Schema Draft 2020-12 manifest, both shipping in publish output for downstream installer and CI consumption.

## What Changed

- **`src/PassReset.Web/appsettings.Production.template.json`** — stripped every `//` comment line (D-15). File now parses as pure JSON via both `ConvertFrom-Json` and the .NET JSON reader. Operator-facing comment content was preserved in `docs/appsettings-Production.md` under a new section. Top-level keys unchanged: `Serilog`, `WebSettings`, `PasswordChangeOptions`, `SmtpSettings`, `EmailNotificationSettings`, `PasswordExpiryNotificationSettings`, `SiemSettings`, `ClientSettings` (8 sections).
- **`src/PassReset.Web/appsettings.schema.json`** (NEW) — JSON Schema Draft 2020-12 manifest covering every options class. Uses only the D-04-restricted keyword set (`type`, `required`, `enum`, `pattern`, `minimum`/`maximum`, `default`, `properties`, `items`, `additionalProperties`). 74 explicit `default` entries. Contains one live obsolete-key example (`Recaptcha` legacy key at line 48, `x-passreset-obsolete-since: "1.3.0"`) so the downstream installer sync can exercise the prompt path end-to-end.
- **`src/PassReset.Web/PassReset.Web.csproj`** — added `<Content Update>` entries for both files with `CopyToOutputDirectory=PreserveNewest` and `CopyToPublishDirectory=PreserveNewest`. `Update` (not `Include`) is required: the Web SDK already auto-includes JSON files as Content, so `Include` raises `NETSDK1022`.

## Schema Sections Covered

| Top-level section | Required? | Depth | Loose? |
|---|---|---|---|
| `WebSettings` | required at root | flat | strict |
| `PasswordChangeOptions` | required at root | flat | strict |
| `SmtpSettings` | required at root | flat | strict |
| `EmailNotificationSettings` | optional | flat | strict |
| `PasswordExpiryNotificationSettings` | optional | flat | strict |
| `SiemSettings` | required at root | nested (`Syslog`, `AlertEmail`) | strict |
| `ClientSettings` | required at root | nested (`Recaptcha`) | strict |
| `Serilog` | optional | open | **loose** (`additionalProperties: true`, no `required`, no property descriptors) |

`Serilog` deliberately left loose — Serilog config is a separate concern (sinks, enrichers, filters) with its own documented schema. Phase 8 scope is PassReset-owned config drift; locking Serilog would create false-positive drift on every Serilog version bump.

## Decisions Made

1. **Real obsolete marker, not placeholder-only.** The plan allowed either a live `x-passreset-obsolete` key or convention-only via `$comment`. A live marker was added (legacy `Recaptcha` field, `x-passreset-obsolete-since: "1.3.0"`) to give the Phase 8-05/06 installer sync an executable test case.
2. **`Content Update` over `Content Include`.** Web SDK auto-inclusion of JSON-as-Content means `Include` raises `NETSDK1022` (duplicate-item error). `Update` overrides copy metadata on the existing SDK-included item — minimal, idiomatic.
3. **Serilog intentionally loose.** Explained above — Serilog versioning lives outside PassReset's change control, so strict validation here would cause CI flakes without catching PassReset drift.

## Defaults Captured (Audit)

74 explicit `default` entries across the schema. Representative samples (full coverage in the schema file):

- `WebSettings.EnableHttpsRedirect`: `true`
- `WebSettings.UseDebugProvider`: `false`
- `PasswordChangeOptions.UseAutomaticContext`: `true`
- `PasswordChangeOptions.LdapPort`: `636`
- `PasswordChangeOptions.PortalLockoutThreshold`: `3`
- `PasswordChangeOptions.PortalLockoutWindow`: `"00:30:00"`
- `PasswordChangeOptions.NotificationEmailStrategy`: `"Mail"`
- `SmtpSettings.Port`: `587`
- `SiemSettings.Syslog.Port`: `514`
- `SiemSettings.Syslog.Protocol`: `"Udp"`
- `ClientSettings.Recaptcha.MinimumScore`: `0.5`
- `PasswordExpiryNotificationSettings.NotificationTimeUtc`: `"09:00"`

## Verification (must_haves)

| Must-have | Check | Result |
|---|---|---|
| Template is pure JSON, zero `//` comments | `Select-String -Pattern '^\s*//' template` | `0` matches |
| Schema exists at expected path | `Test-Path src/PassReset.Web/appsettings.schema.json` | `True` |
| Schema is Draft 2020-12 | `grep '"$schema": "https://json-schema.org/draft/2020-12/schema"'` | `1` match |
| Schema validates template | `Test-Json -Path template -SchemaFile schema` | `True` |
| No forbidden keywords (`if`/`then`/`else`/`oneOf`/`anyOf`/`format`) | grep | `0` matches |
| ≥10 defaults in schema | `grep -c '"default":'` | `74` |
| Both files ship in publish output | `dotnet publish` + `ls publish-dir` | Both present, byte-match source |
| `dotnet build` green | `dotnet build --configuration Release` | `0 errors` (10 pre-existing xUnit warnings in tests, out of scope) |

## Commits

| Task | Commit | Message |
|---|---|---|
| 1 | `e81b839` | `refactor(web): strip // comments from production template (D-15)` |
| 2 | `fcd704b` | `feat(web): add appsettings.schema.json manifest (D-01..D-04, D-11)` |
| 3 | `b9deb9d` | `build(web): ship appsettings schema + template in publish output` |

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] `Content Include` triggered NETSDK1022 duplicate-item error**

- **Found during:** Task 3 build verification
- **Issue:** Plan specified `<Content Include="...">` for both files. ASP.NET Core Web SDK auto-includes JSON files as Content items by default, so the explicit `Include` caused `error NETSDK1022: Duplicate Content items were included`.
- **Fix:** Switched to `<Content Update="...">` which modifies the metadata on the already-included SDK item rather than adding a duplicate. Added inline comment in csproj explaining the Web-SDK-glob behavior.
- **Files modified:** `src/PassReset.Web/PassReset.Web.csproj`
- **Commit:** `b9deb9d` (single commit covers both the original intent and the fix — the broken Include never reached a commit)

### Deferred Items (Out of Scope)

- 10 pre-existing xUnit1051 warnings in `src/PassReset.Tests/Web/Controllers/PasswordControllerTests.cs` (should use `TestContext.Current.CancellationToken`). Not caused by this plan; belongs in a separate test-hygiene task.

## Lessons / Follow-ups

- **Downstream plan 08-07 (Publish packaging):** `Publish-PassReset.ps1` already works correctly for schema shipping because the csproj now copies it via `CopyToPublishDirectory`. The explicit `Copy-Item $schemaPath` hinted at in `08-PATTERNS.md` may be redundant — verify during plan 07 execution; a single source of truth (csproj) is preferable to dual copy paths.
- **Downstream plan 08-02 (CI validation):** `Test-Json` on `windows-latest` has PowerShell 7.4+ available by default — no `#Requires` workaround needed.
- **Downstream plan 08-05/06 (installer sync):** the live `x-passreset-obsolete` marker on the legacy `Recaptcha` field gives the sync code an immediate test case for the "Remove obsolete key? [y/N]" prompt path.

## Self-Check: PASSED

Verified:
- `src/PassReset.Web/appsettings.schema.json` — FOUND
- `src/PassReset.Web/appsettings.Production.template.json` — FOUND (pure JSON)
- `src/PassReset.Web/PassReset.Web.csproj` — FOUND (Content Update entries present)
- Commit `e81b839` — FOUND in git log
- Commit `fcd704b` — FOUND in git log
- Commit `b9deb9d` — FOUND in git log
- `Test-Json` validation — `True`
- `dotnet publish` — both artifacts land in publish root
