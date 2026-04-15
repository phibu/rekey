---
phase: 03-v1-3-ux-features
plan: 01
subsystem: branding
tags: [feat-001, branding, static-files, installer]
requires: []
provides:
  - "BrandingSettings (8 nullable string fields) on ClientSettings"
  - "/brand/* static-file route via PhysicalFileProvider rooted at C:\\ProgramData\\PassReset\\brand\\ (or AssetRoot override)"
  - "Install-PassReset.ps1 creates brand directory on fresh install; preserves on upgrade"
  - "BrandHeader component with logo + onError fallback to LockPersonIcon"
  - "Runtime favicon injection via document.head <link rel='icon'>"
  - "Helpdesk URL/email block (hidden when both absent)"
  - "Usage text block (replaces default helper when set)"
  - "Footer company name override"
affects:
  - src/PassReset.Web/Models/ClientSettings.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/appsettings.Production.template.json
  - src/PassReset.Web/appsettings.json
  - deploy/Install-PassReset.ps1
  - docs/appsettings-Production.md
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/App.tsx
key_files_created:
  - src/PassReset.Web/ClientApp/src/components/BrandHeader.tsx
key_files_modified:
  - src/PassReset.Web/Models/ClientSettings.cs
  - src/PassReset.Web/Program.cs
  - src/PassReset.Web/appsettings.Production.template.json
  - src/PassReset.Web/appsettings.json
  - deploy/Install-PassReset.ps1
  - docs/appsettings-Production.md
  - src/PassReset.Web/ClientApp/src/types/settings.ts
  - src/PassReset.Web/ClientApp/src/App.tsx
  - src/PassReset.Web/ClientApp/src/components/ErrorBoundary.test.tsx
decisions:
  - "PhysicalFileProvider mounted via second app.UseStaticFiles call after the default wwwroot files — order matters because /brand/* prefix routes before the SPA fallback."
  - "ServeUnknownFileTypes = false on the brand provider so dropping non-image files (e.g., HTML) doesn't expose them via /brand/."
  - "Brand directory created on every installer run (Test-Path guard) — both fresh install AND upgrade. Never removed; never overwrites existing files."
  - "appsettings.json (JSONC, comments allowed) shows the Branding block as a commented-out example. appsettings.Production.template.json (strict JSON used by template) ships an empty-but-present Branding block as the operator starting point."
  - "BrandHeader uses local React state (logoFailed) reset via useEffect when logoFileName changes — guards against transient image failures locking out the icon fallback when the operator fixes the file."
  - "Footer companyName override falls back to 'Internal IT Tool' (existing default) when not set."
  - "Fixed pre-existing test file ErrorBoundary.test.tsx (from 02-04) — added `import type { JSX } from 'react'` so React 19 build passes. JSX is no longer a global namespace in @types/react 19."
metrics:
  tasks_completed: 2
  files_created: 1
  files_modified: 9
  completed: "2026-04-15"
deviations:
  - "Pre-existing TypeScript build error in ErrorBoundary.test.tsx (JSX namespace) was blocking the build — fixed inline as part of this plan since it had to compile clean to verify task 2."
---

# Phase 03 Plan 01: Operator Branding Summary

## What was built

**Backend (Task 1):**
- `BrandingSettings` sealed class on `ClientSettings` with 8 nullable string fields (CompanyName, PortalName, HelpdeskUrl, HelpdeskEmail, UsageText, LogoFileName, FaviconFileName, AssetRoot).
- Second `app.UseStaticFiles()` call in `Program.cs` mounts a `PhysicalFileProvider` rooted at `C:\ProgramData\PassReset\brand\` (or `AssetRoot` override) under request path `/brand`. Directory auto-created at startup.
- `Install-PassReset.ps1` provisions `$env:ProgramData\PassReset\brand` on every run with a `Test-Path` guard — no-op on upgrade if the directory already exists.
- `appsettings.Production.template.json` ships an empty Branding block; `appsettings.json` documents it as a commented-out example.
- `docs/appsettings-Production.md` gains a new "Branding (FEAT-001)" subsection covering all 8 keys, the upgrade-safe path, and the omit-to-default rule.

**Frontend (Task 2):**
- `BrandingSettings` interface + `branding?: BrandingSettings` on `ClientSettings`.
- `BrandHeader.tsx` component renders `<img src={`/brand/${logoFile}`}>` when configured (with `onError` fallback to `LockPersonIcon` + `portalName`), else the icon+text default.
- `App.tsx` runtime favicon injection via `useEffect` querying/creating `<link rel="icon">`.
- Helpdesk block (Stack of MUI Typography links) rendered between card and footer, hidden when both URL and email absent.
- Usage text block above the form replaces the default helper when set.
- Footer copy uses `branding?.companyName ?? 'Internal IT Tool'`.

## Verification

- `dotnet build src/PassReset.sln --configuration Release` → 0 errors
- `cd src/PassReset.Web/ClientApp && npm run build` → tsc + Vite green
- `npm test` → 45/45 tests pass (no regressions from 02-04 suite)
- Manual: omitting Branding block → UI identical to v1.2.3 (LockPersonIcon + "PassReset")

## Known limitations

- No automated test for runtime favicon injection (DOM-mutation effect) — covered by manual checklist above.
- BrandHeader does not validate logo aspect ratio; CSS clamps to `maxHeight: 48` / `maxWidth: 200` with `objectFit: contain`.
