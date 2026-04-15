---
phase: 02-v1-3-test-foundation
plan: 01
subsystem: testing
tags: [xunit, dotnet, scaffolding]
requires: []
provides:
  - "PassReset.Tests project wired into src/PassReset.sln"
  - "public partial class Program marker for WebApplicationFactory<Program>"
  - "xUnit v3 test runner proven working via smoke test"
affects:
  - src/PassReset.sln
  - src/PassReset.Web/Program.cs
tech_stack_added:
  - "xunit.v3 3.2.2"
  - "Microsoft.NET.Test.Sdk 17.11.*"
  - "xunit.runner.visualstudio 3.*"
  - "coverlet.msbuild 8.0.1"
  - "NSubstitute 5.3.0"
  - "Microsoft.AspNetCore.Mvc.Testing 10.0.*"
key_files_created:
  - src/PassReset.Tests/PassReset.Tests.csproj
  - src/PassReset.Tests/GlobalUsings.cs
  - src/PassReset.Tests/SmokeTests.cs
key_files_modified:
  - src/PassReset.sln
  - src/PassReset.Web/Program.cs
decisions:
  - "Used coverlet.msbuild (not coverlet.collector) per research CONTEXT D6 override"
  - "Kept xunit.v3 (no downgrade to v2)"
  - "OutputType=Exe required by xunit.v3 runner model"
metrics:
  tasks_completed: 3
  files_created: 3
  files_modified: 2
  completed: "2026-04-15"
---

# Phase 02 Plan 01: xUnit Test Project Scaffolding Summary

Scaffolded the `PassReset.Tests` xUnit v3 project, wired it into the solution, and added the `public partial class Program {}` marker required by `WebApplicationFactory<Program>`. Smoke test `TestRunner_Works` passes end-to-end via `dotnet test`.

## What Changed

- Created `src/PassReset.Tests/PassReset.Tests.csproj` targeting `net10.0-windows` with xunit.v3 3.2.2, coverlet.msbuild 8.0.1, NSubstitute 5.3.0, Mvc.Testing 10.0.*, and project references to Common/PasswordProvider/Web.
- Created `src/PassReset.Tests/GlobalUsings.cs` with `global using Xunit;` and `global using NSubstitute;`.
- Created `src/PassReset.Tests/SmokeTests.cs` with a single `TestRunner_Works` fact.
- Added the test project to `src/PassReset.sln`.
- Appended `public partial class Program { }` to `src/PassReset.Web/Program.cs` at the global namespace level (below the top-level statements and `finally` block).

## Verification Results

- `dotnet restore src/PassReset.Tests/PassReset.Tests.csproj` — succeeded (packages resolved without version overrides).
- `dotnet build src/PassReset.sln -c Release` — 0 errors, 0 warnings.
- `dotnet test src/PassReset.sln -c Release --no-build --filter FullyQualifiedName=PassReset.Tests.SmokeTests.TestRunner_Works` — `Bestanden! Fehler: 0, erfolgreich: 1`.

## Deviations from Plan

None — plan executed exactly as written. No package version fallbacks needed; no MTP-vs-VSTest workaround needed; xunit.v3 + coverlet.msbuild restored cleanly and `dotnet test` discovered the smoke test on first try.

## Commits

- `e3c79b1` — test(02-01): scaffold xUnit v3 test project
- `fbd9d32` — test(02-01): wire test project into solution and add Program partial marker

## Self-Check: PASSED

- FOUND: src/PassReset.Tests/PassReset.Tests.csproj
- FOUND: src/PassReset.Tests/GlobalUsings.cs
- FOUND: src/PassReset.Tests/SmokeTests.cs
- FOUND: commit e3c79b1
- FOUND: commit fbd9d32
- VERIFIED: `public partial class Program` present in Program.cs
- VERIFIED: PassReset.Tests entry in src/PassReset.sln
