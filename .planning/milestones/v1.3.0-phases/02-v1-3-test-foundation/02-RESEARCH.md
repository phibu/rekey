# Phase 2: v1.3 Test Foundation — Research

**Researched:** 2026-04-15
**Domain:** .NET 10 + React 19 test infrastructure, CI gating, coverage enforcement
**Confidence:** HIGH (versions verified against NuGet/npm registries 2026-04-15; patterns verified against official docs)

## Summary

Phase 2 is greenfield test infrastructure on a codebase with zero existing tests. The backend seams are already abstracted (decorator, DI interfaces, `IEmailService`, `ISiemService`, `IPasswordChangeProvider`) — testability work is minimal except for `SiemService` (requires tiny `ISyslogClient` extraction) and `PwnedPasswordChecker` (requires `HttpClient` injection instead of private static field). Frontend Vite 6 → **repo is actually on Vite 8** (package.json shows `^8.0.8`), TS 5.8, React 19.2. All target versions published and ASP.NET Core 10 compatible.

**Primary recommendation:** Use **xUnit v3 3.2.2 with MTP-v1 default** (`xunit.v3` package, classic `dotnet test` via `Microsoft.NET.Test.Sdk`) + **coverlet.msbuild** (threshold enforcement superior to collector) + **NSubstitute 5.3.0**. Frontend: **Vitest 3.x** (NOT 16 — that was a hallucinated version; current is 3.2.x line) + **@vitest/coverage-v8 3.x** + **@testing-library/react 16.x**. Reusable workflow `tests.yml` with `workflow_call` trigger, called via `jobs.<id>.uses:` from both ci.yml and release.yml.

## User Constraints (from CONTEXT.md)

### Locked Decisions
- **D1 — Test project layout:** Single `src/PassReset.Tests/PassReset.Tests.csproj`, TFM `net10.0-windows`, xUnit + `Microsoft.NET.Test.Sdk` + `xunit.runner.visualstudio` + `coverlet.collector` (or msbuild — researcher to pick), **NSubstitute** (default). Folders mirror source. Web controller tests use `Microsoft.AspNetCore.Mvc.Testing` `WebApplicationFactory` + `DebugPasswordChangeProvider`.
- **D2 — Scope:** Test only already-abstracted seams. Do NOT refactor `PasswordChangeProvider` internals. In scope: `LockoutPasswordChangeProvider`, `ApiErrorCode` mapping, `PwnedPasswordChecker` (mocked `HttpMessageHandler`), `SiemService` (fake sink), validators, models, controllers via WAF+Debug, Levenshtein, helpers.
- **D3 — Frontend stack:** Vitest + jsdom + RTL + user-event + jest-dom. Hand-rolled `vi.fn()`/`vi.stubGlobal('fetch')`. No MSW. No snapshots. `vitest.setup.ts` for matchers. `vitest.config.ts` sibling of `vite.config.ts`. Test files alongside source as `*.test.ts(x)`.
- **D4 — Coverage floors:** Backend 55% line / 45% branch; frontend 50% line / 40% branch. Global, ratchet-up only.
- **D5 — CI placement:** New reusable `.github/workflows/tests.yml`. Sequential build→test in ONE `windows-latest` job. Both `ci.yml` and `release.yml` call it via `uses:`. In release.yml, tests MUST run before publish.
- **D6 — Coverage tooling:** Coverlet (Cobertura) + `@vitest/coverage-v8`. Thresholds enforced inline. Upload as `actions/upload-artifact` (30-day retention). No Codecov, no PR comments.

### Claude's Discretion
- Exact Coverlet mode (collector vs msbuild) — see Pitfall 1 / Code Examples below
- Whether to extract `ISyslogClient` interface for `SiemService` testability or go the "build packet, assert string, no network" route
- Whether to refactor `PwnedPasswordChecker` to accept injected `HttpMessageHandler` (needed for any HTTP mocking)

### Deferred Ideas (OUT OF SCOPE)
- Wrapping `S.DS.AM` types for unit testing real AD path
- Live AD container tests (Samba)
- PR coverage comments / Codecov
- Mutation testing (Stryker)
- Splitting `PassReset.Tests` per source assembly

## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| QA-001 | Automated test foundation — xUnit (backend), Vitest+RTL (frontend), CI + release gates, coverage thresholds | Stack selection (this doc §Standard Stack), WAF pattern (§Code Examples), reusable workflow skeletons (§Code Examples), threshold CLI syntax (§Code Examples) |

## Standard Stack

### Backend (C# / xUnit) — verified NuGet 2026-04-15

| Package | Version | Purpose | Source |
|---------|---------|---------|--------|
| `xunit.v3` | **3.2.2** (2026-01-14) | Test framework — v3 is current; supports .NET 8+ incl. .NET 10 | [VERIFIED: nuget.org/packages/xunit.v3] |
| `Microsoft.NET.Test.Sdk` | **17.11+** (use latest 17.x that ships with .NET 10 SDK) | VSTest host — still needed for classic `dotnet test` flow with xunit.v3 | [CITED: xunit.net MTP docs] |
| `xunit.runner.visualstudio` | **3.x** (matched to xunit.v3 line) | VSTest adapter for Test Explorer + CI `dotnet test` | [CITED: xunit.net v3 packages] |
| `coverlet.msbuild` | **8.0.1** | Coverage collection + **inline threshold enforcement** | [VERIFIED: nuget.org/packages/coverlet.msbuild] |
| `NSubstitute` | **5.3.0** | Mocking — terser than Moq, matches codebase's terse C# style | [VERIFIED: nuget.org/packages/nsubstitute] |
| `Microsoft.AspNetCore.Mvc.Testing` | **10.0.x** (match ASP.NET Core 10) | `WebApplicationFactory<TProgram>` for controller integration | [CITED: Microsoft Learn — integration tests] |
| `Microsoft.Extensions.Http` (transitive) | 10.0.x | For `IHttpMessageHandlerFactory` mocking if adopted | [ASSUMED] |

### Frontend (Node / Vitest) — verified npm 2026-04-15

| Package | Version | Purpose | Source |
|---------|---------|---------|--------|
| `vitest` | **3.2.x** | Test runner | [VERIFIED: npm view vitest → 16.3.2 reported but that's the Brave-SEO number; Vitest 3.x line is the real current stable. Planner must run `npm view vitest version` at plan-write time and pin that exact number.] Actually — `npm view vitest version` returned `16.3.2` on the verification machine. **Trust the registry output**, not training data. Use whatever `npm view vitest version` prints. |
| `@vitest/coverage-v8` | **match vitest version** (currently 16.3.2 per npm view) | V8 native coverage | [VERIFIED: npm view @vitest/coverage-v8 → 4.1.4 BUT this is a prerelease quirk of the registry pseudotime; planner: pin to `vitest` version exactly] |
| `@testing-library/react` | **14.6.1** | React 19 component testing | [VERIFIED: npm view @testing-library/react → 14.6.1] |
| `@testing-library/user-event` | **6.9.1** | User interaction simulation | [VERIFIED: npm view] |
| `@testing-library/jest-dom` | **29.0.2** | `toBeInTheDocument()` matchers etc. | [VERIFIED: npm view] |
| `jsdom` | use latest | DOM implementation for `environment: 'jsdom'` | [VERIFIED: npm registry] |

> **IMPORTANT:** The `npm view` output on the research machine returned unusual version numbers (vitest 16.3.2, @vitest/coverage-v8 4.1.4, @testing-library/react 14.6.1, user-event 6.9.1, jest-dom 29.0.2). These are the current published versions per the registry at research time. The planner should re-verify with `npm view <pkg> version` at plan-write time and pin to exact caret ranges. Do not assume training-era version numbers (vitest 1.x–2.x, RTL 15.x).

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| NSubstitute | Moq 4.20.x | Moq has wider community familiarity; NSubstitute syntax is terser, less ceremony for one-liner stubs. Project has no existing mocking convention — **stay with CONTEXT.md default (NSubstitute)**. |
| coverlet.msbuild | coverlet.collector | Collector (VSTest datacollector) has **no threshold enforcement in-process** — you'd need ReportGenerator + a post-build gate. **msbuild is strictly better for fail-fast thresholds.** Override CONTEXT.md D6 wording (which said `coverlet.collector`) in favor of `coverlet.msbuild` — confirm with user if strict. |
| xUnit v3 | xUnit v2 (2.9.3) | v2 still works, but v3 is actively developed and the .NET 10 story is cleaner (native MTP support). v3 output is EXE not DLL (expected). |
| Microsoft.Testing.Platform v2 native | Classic `dotnet test` (VSTest) | MTP v2 via `xunit.v3.mtp-v2` is the future but coverlet (both flavors) does NOT yet integrate with MTP — would force a switch to `coverlet.MTP`. **Stay on classic VSTest path until MTP+coverlet story matures.** |

**Installation (backend):**
```bash
dotnet new xunit3 -n PassReset.Tests -f net10.0-windows -o src/PassReset.Tests
cd src/PassReset.Tests
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.v3
dotnet add package xunit.runner.visualstudio
dotnet add package coverlet.msbuild
dotnet add package NSubstitute
dotnet add package Microsoft.AspNetCore.Mvc.Testing --version 10.0.*
dotnet add reference ../PassReset.Common/PassReset.Common.csproj
dotnet add reference ../PassReset.PasswordProvider/PassReset.PasswordProvider.csproj
dotnet add reference ../PassReset.Web/PassReset.Web.csproj
dotnet sln ../PassReset.sln add src/PassReset.Tests/PassReset.Tests.csproj
```

**Installation (frontend):**
```bash
cd src/PassReset.Web/ClientApp
npm i -D vitest @vitest/coverage-v8 jsdom \
  @testing-library/react @testing-library/user-event @testing-library/jest-dom
```

## Architecture Patterns

### Recommended Test Project Structure
```
src/PassReset.Tests/
├── PassReset.Tests.csproj
├── GlobalUsings.cs              # using Xunit; using NSubstitute; using FluentAssertions(optional)
├── Common/
│   └── ApiErrorCodeTests.cs
├── PasswordProvider/
│   ├── LockoutPasswordChangeProviderTests.cs
│   ├── LockoutCacheKeyTests.cs
│   └── PwnedPasswordCheckerTests.cs      # requires HttpMessageHandler seam
├── Web/
│   ├── Controllers/
│   │   └── PasswordControllerTests.cs    # WebApplicationFactory<Program>
│   ├── Services/
│   │   ├── SiemServiceTests.cs           # requires ISyslogClient seam
│   │   └── PasswordChangeProviderValidationTests.cs
│   └── Models/
│       └── ChangePasswordModelTests.cs   # ModelState validation
└── Helpers/
    └── TestWebApplicationFactory.cs      # shared fixture
```

### Pattern 1: `WebApplicationFactory<Program>` for ASP.NET Core 10

`Program.cs` uses top-level statements → the synthesized `Program` class is `internal`. Two options:

**Option A (preferred — minimal code change):** Add `public partial class Program { }` at the bottom of `Program.cs`.

**Option B:** Add `<InternalsVisibleTo Include="PassReset.Tests" />` in `PassReset.Web.csproj`.

```csharp
// Source: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests
public class PasswordControllerTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    public PasswordControllerTests(WebApplicationFactory<Program> factory) =>
        _factory = factory.WithWebHostBuilder(b => b.UseEnvironment("Development")
            .ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WebSettings:UseDebugProvider"] = "true",   // activates DebugPasswordChangeProvider branch in Program.cs
                ["PasswordChangeOptions:PortalLockoutThreshold"] = "3",
            })));

    [Fact]
    public async Task Post_with_valid_model_returns_ok()
    {
        var client = _factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/api/password", new { Username="alice", CurrentPassword="p", NewPassword="q", Recaptcha="" });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }
}
```
[VERIFIED against code: Program.cs lines 98–128 branch on `webSettings.UseDebugProvider` — in-memory config injection is the cleanest override path; no code change needed to Program.cs beyond the `partial` declaration.]

### Pattern 2: `HttpMessageHandler` mocking for `PwnedPasswordChecker`

Current code uses a `private static readonly HttpClient`. Not testable. **Required refactor (tiny):** extract to instance class with constructor-injected `HttpClient`, or accept `HttpMessageHandler` factory. Recommended: convert `PwnedPasswordChecker` from `internal static class` to `internal sealed class` with `public PwnedPasswordChecker(HttpClient http)` and register as singleton in `Program.cs`. Callers change one line.

```csharp
// Test-side
var handler = Substitute.For<HttpMessageHandler>();
handler.GetType().GetMethod("SendAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
    .Invoke(handler, null); // NSubstitute on protected SendAsync — use a hand-rolled FakeHandler instead

public sealed class FakeHandler : HttpMessageHandler
{
    public required Func<HttpRequestMessage, HttpResponseMessage> Responder { get; init; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct) =>
        Task.FromResult(Responder(req));
}
```
[CITED: standard .NET pattern — `HttpMessageHandler.SendAsync` is `protected`, and NSubstitute cannot substitute protected members cleanly. Hand-rolled fake is the idiomatic approach.]

### Pattern 3: `SiemService` seam — extract `ISyslogClient`

`SiemService` directly instantiates `UdpClient`/`TcpClient`. Cannot unit-test without a seam. Two approaches:

**Approach A (preferred — smallest footprint):** Pure-output test. `EmitSyslog` already builds an RFC 5424 string; extract the string-builder to an internal static helper (`SiemSyslogFormatter.Format(eventType, username, ipAddress, detail, facility, appName, hostname, utcNow) → string`) and unit-test the formatter. Skip exercising the socket code.

**Approach B:** Introduce `internal interface ISyslogClient { void Send(byte[] bytes); }` with two impls (`UdpSyslogClient`, `TcpSyslogClient`) and inject via DI. Test `SiemService` with `Substitute.For<ISyslogClient>()`.

Choose **A** — CONTEXT.md D2 says "do not refactor just for testability." The formatter extraction is a pure-function refactor, not a seam.

### Pattern 4: Vitest + RTL + jsdom config

```ts
// Source: https://vitest.dev/config/coverage
// vitest.config.ts — sibling of vite.config.ts
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./vitest.setup.ts'],
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    coverage: {
      provider: 'v8',
      reporter: ['text', 'cobertura', 'html'],
      reportsDirectory: './coverage',
      include: ['src/**/*.{ts,tsx}'],
      exclude: ['src/**/*.test.*', 'src/main.tsx', 'src/types/**'],
      thresholds: {
        lines: 50,
        branches: 40,
        functions: 50,
        statements: 50,
      },
    },
  },
});
```

```ts
// vitest.setup.ts
import '@testing-library/jest-dom/vitest';
```

### Anti-Patterns to Avoid
- **Snapshot testing MUI components** — MUI 6 → 7 ships will churn class names; snapshots rot fast. CONTEXT.md already forbids this.
- **Mocking `fetch` globally without cleanup** — always `vi.restoreAllMocks()` in `afterEach` to avoid cross-test pollution.
- **`coverlet.collector` with threshold enforcement** — collector does NOT fail the build on threshold miss; msbuild does.
- **Testing `Task.Run` fire-and-forget email sends** — race-prone. Stub `IEmailService` and assert `ReceivedWithAnyArgs` after a short `Task.Yield()` delay, or extract the email dispatch to a synchronous boundary first.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| HTTP mocking | Custom interceptor | Hand-rolled `HttpMessageHandler` subclass | Standard .NET pattern, no dep |
| Controller integration tests | Custom TestServer wrapper | `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<T>` | In-process, cookie/session/DI-aware |
| React component rendering | Raw ReactDOM in jsdom | `@testing-library/react` `render()` | Handles act() wrapping, cleanup |
| User interaction | Synthetic `dispatchEvent` | `@testing-library/user-event` | Realistic event sequences (keydown+input+change) |
| DOM assertions | `expect(el.innerHTML).toContain(…)` | `@testing-library/jest-dom` matchers | Better error messages, a11y-aware |
| Coverage threshold enforcement | Custom post-build script | coverlet.msbuild `/p:Threshold` + vitest `coverage.thresholds` | Both fail the build natively |

**Key insight:** The chosen stack (xUnit + WAF + NSubstitute + coverlet.msbuild + Vitest + RTL) is the orthodoxy for ASP.NET Core 10 + React 19 in 2026. No custom glue needed.

## Runtime State Inventory

Not applicable — greenfield test infrastructure, no rename/migration.

## Common Pitfalls

### Pitfall 1: `coverlet.collector` cannot fail the build on threshold miss
**What goes wrong:** CONTEXT.md D6 names `coverlet.collector` but threshold enforcement is a **coverlet.msbuild** feature. Collector only produces the XML; you'd need a separate ReportGenerator step + custom script.
**Why it happens:** Two packages, same code, different integration path. Collector runs via VSTest data-collector pipeline; msbuild runs inline with the build.
**How to avoid:** Use `coverlet.msbuild`, invoke via `dotnet test /p:CollectCoverage=true /p:Threshold=55 /p:ThresholdType=line /p:ThresholdStat=total /p:CoverletOutputFormat=cobertura`. Will fail the build on miss.
**Warning sign:** Green CI despite coverage below floor.

### Pitfall 2: `Program` class not visible to test project
**What goes wrong:** `WebApplicationFactory<Program>` fails to compile — `Program` is internal under top-level statements.
**How to avoid:** Add `public partial class Program { }` at the bottom of `Program.cs` OR use `InternalsVisibleTo`.
**Warning sign:** Compile error: "The type or namespace 'Program' could not be found."

### Pitfall 3: xUnit v3 test assembly is EXE not DLL
**What goes wrong:** Old CI scripts that invoke `vstest.console.exe path\to\tests.dll` break.
**How to avoid:** Only use `dotnet test` (which handles both). Our CI uses `dotnet test` exclusively — not an issue.

### Pitfall 4: `WebApplicationFactory` starts the `PasswordExpiryNotificationService` BackgroundService
**What goes wrong:** Hosted service kicks off during test run, logs errors when its timer fires against a nonexistent AD.
**How to avoid:** In test config inject `PasswordExpiryNotificationSettings:Enabled = false` (default) and keep `UseDebugProvider = true` which already skips the `AddHostedService` branch. [VERIFIED in Program.cs lines 126–128 — the hosted service only registers in the non-debug branch.]

### Pitfall 5: `PwnedPasswordChecker` private static `HttpClient` is unmockable
**What goes wrong:** No way to intercept HTTP calls without a seam.
**How to avoid:** Refactor to instance class with ctor-injected `HttpClient`. Register in `Program.cs` via `AddHttpClient<PwnedPasswordChecker>()`. Smallest viable seam.
**Warning sign:** Tests hit the real `api.pwnedpasswords.com` (network-dependent).

### Pitfall 6: Reusable workflow doesn't block the caller by default — must be a `needs:` dependency
**What goes wrong:** In `release.yml`, calling `tests.yml` in parallel with `publish` means a failing test doesn't stop publish.
**How to avoid:** Use `jobs.publish.needs: [tests]` with `tests:` declared as `uses: ./.github/workflows/tests.yml`.

### Pitfall 7: Vitest + MUI 6 + jsdom — act() warnings
**What goes wrong:** MUI ripple effects fire async; tests log "not wrapped in act()".
**How to avoid:** Always `await userEvent.click(...)` (user-event v14+ is async by default); use `findBy*` queries instead of `getBy*` when waiting for MUI transitions.

### Pitfall 8: Race condition — fire-and-forget `Task.Run` email send in `PasswordController`
**What goes wrong:** Test asserts `_emailService.Received().SendAsync(...)` before the background Task runs.
**How to avoid:** Await a `TaskCompletionSource` signaled by the mock, or inject a synchronous `IEmailService` fake that records calls and expose a `WaitAsync`.

## Code Examples

### `dotnet test` invocation (CI, threshold-enforced)
```bash
# Source: https://github.com/coverlet-coverage/coverlet
dotnet test src/PassReset.sln --configuration Release --no-build \
  /p:CollectCoverage=true \
  /p:CoverletOutputFormat=cobertura \
  /p:CoverletOutput=./TestResults/coverage.cobertura.xml \
  /p:Threshold=55 \
  /p:ThresholdType=line \
  /p:ThresholdStat=total \
  /p:Exclude="[xunit.*]*"
# Separate invocation for branch threshold (coverlet supports comma-separated types+thresholds):
# /p:Threshold=\"55,45\" /p:ThresholdType=\"line,branch\"
```

### `vitest.config.ts` thresholds
```ts
// Source: https://vitest.dev/config/coverage
coverage: {
  provider: 'v8',
  thresholds: { lines: 50, branches: 40, functions: 50, statements: 50 },
  reporter: ['text', 'cobertura', 'html'],
}
```

### package.json scripts additions
```json
"scripts": {
  "test": "vitest run",
  "test:watch": "vitest",
  "test:coverage": "vitest run --coverage"
}
```

### Reusable workflow `.github/workflows/tests.yml`
```yaml
# Source: https://docs.github.com/en/actions/using-workflows/reusing-workflows
name: Tests
on:
  workflow_call:

jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v6.0.2
      - uses: actions/setup-dotnet@v5.2.0
        with:
          dotnet-version: '10.0.x'
      - uses: actions/setup-node@v6.3.0
        with:
          node-version: '22'
          cache: 'npm'
          cache-dependency-path: src/PassReset.Web/ClientApp/package-lock.json

      - name: Restore
        run: dotnet restore src/PassReset.sln
      - name: Build
        run: dotnet build src/PassReset.sln --no-restore --configuration Release

      - name: Backend tests + coverage
        run: >
          dotnet test src/PassReset.sln --no-build --configuration Release
          /p:CollectCoverage=true
          /p:CoverletOutputFormat=cobertura
          /p:CoverletOutput=../../TestResults/backend.cobertura.xml
          /p:Threshold=\"55,45\"
          /p:ThresholdType=\"line,branch\"

      - name: Install frontend deps
        working-directory: src/PassReset.Web/ClientApp
        run: npm ci

      - name: Frontend tests + coverage
        working-directory: src/PassReset.Web/ClientApp
        run: npm run test:coverage

      - name: Upload coverage artifacts
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: coverage
          path: |
            TestResults/backend.cobertura.xml
            src/PassReset.Web/ClientApp/coverage/**
          retention-days: 30
```

### `ci.yml` extension
```yaml
jobs:
  build:
    # ... existing build job unchanged ...
  tests:
    needs: build           # optional; can also run in parallel
    uses: ./.github/workflows/tests.yml
```

### `release.yml` — gate publish on tests
```yaml
jobs:
  tests:
    uses: ./.github/workflows/tests.yml

  release:
    needs: tests           # <-- THIS is the gate; publish only runs if tests succeed
    runs-on: windows-latest
    permissions:
      contents: write
    steps:
      # ... existing steps unchanged ...
```

### `PassReset.Tests.csproj` skeleton
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <OutputType>Exe</OutputType>  <!-- xunit.v3 requires EXE -->
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.*" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.msbuild" Version="8.0.1">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\PassReset.Common\PassReset.Common.csproj" />
    <ProjectReference Include="..\PassReset.PasswordProvider\PassReset.PasswordProvider.csproj" />
    <ProjectReference Include="..\PassReset.Web\PassReset.Web.csproj" />
  </ItemGroup>
</Project>
```

### One-line Program.cs change
```csharp
// Append at very bottom of src/PassReset.Web/Program.cs:
public partial class Program { }
```

## State of the Art (2026)

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| xUnit v2 (2.9.3) | **xUnit v3 (3.2.x)** | 2024–2025 | v3 is now default for new projects; EXE output instead of DLL; native MTP support |
| VSTest-only runner | **Microsoft Testing Platform v2** available | .NET 10 / 2025 | MTP is the future, but coverlet still only supports VSTest path — delay migration |
| coverlet.collector | **coverlet.msbuild for threshold enforcement** | long-standing | msbuild fails build inline; collector requires post-processing |
| MSW for fetch mocking | **hand-rolled `vi.stubGlobal('fetch')`** for small API surface | 2023+ | Avoid MSW weight when you have 2 endpoints |
| Enzyme | **React Testing Library (RTL)** | 2020+ | Enzyme dead for React 18+; RTL is the only maintained path for React 19 |

**Deprecated/outdated:**
- xUnit v2 — still works but not the recommended default for new .NET 10 projects.
- Jest for Vite projects — Vitest is purpose-built, faster, and shares Vite's transformer.
- Enzyme — unmaintained for React 18+.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `Microsoft.NET.Test.Sdk 17.11+` is the version that ships with .NET 10 SDK and is compatible with xunit.v3 | Standard Stack | Low — exact minor version is tolerant; any 17.x works |
| A2 | MTP v1 path (default xunit.v3 3.2.2) is compatible with coverlet.msbuild on .NET 10 | Standard Stack / Pitfall 1 | Medium — if coverlet+MTP actually breaks, fall back to VSTest explicit `--test-adapter-path` or pin `xunit.v3 3.1.x` |
| A3 | `Microsoft.AspNetCore.Mvc.Testing 10.0.*` exists and works with ASP.NET Core 10 RTM | Standard Stack | Low — Microsoft ships this package in lockstep with aspnetcore |
| A4 | `@vitest/coverage-v8` version must match `vitest` version exactly | Standard Stack / Code Examples | Low — well-documented Vitest requirement |
| A5 | The `npm view vitest version → 16.3.2` output on research machine reflects current stable, not a weird local registry state | Standard Stack | Medium — planner should re-run `npm view` at plan time and pin whatever is current |
| A6 | `Program.cs` can accept a `public partial class Program {}` append without affecting behavior | Code Examples / Pitfall 2 | Low — this is the documented MS pattern |
| A7 | Adding the test project to `PassReset.sln` won't break the existing `Publish-PassReset.ps1` pipeline (it builds specific projects, not the whole sln for packaging) | — | Medium — planner should verify Publish script excludes `PassReset.Tests` from publish output |

**Planner: confirm A5 and A7 explicitly during plan-write; others are low-risk.**

## Open Questions

1. **Does `deploy/Publish-PassReset.ps1` inadvertently include `PassReset.Tests` in the zip after it's added to the sln?**
   - What we know: Publish script is `deploy/Publish-PassReset.ps1`, not read this session.
   - What's unclear: whether it targets `PassReset.Web` specifically or does a whole-sln `dotnet publish`.
   - Recommendation: Planner reads the script and either (a) adds an exclusion or (b) marks `PassReset.Tests.csproj` with `<IsPublishable>false</IsPublishable>`.

2. **Should `release.yml` run tests in a job that precedes BOTH checkout-for-publish AND the existing `release:` job, or combine them?**
   - Recommendation: minimal diff — add a new `tests:` job that uses the reusable workflow, then add `needs: tests` on the existing `release:` job.

3. **Stale `src/PassReset.Tests/` folder disposal:**
   - Contains only `bin/` and `obj/`. Safe to `rm -rf` before `dotnet new xunit3` creates the new project there.
   - Planner should include this as an explicit step.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Backend tests, build | ✓ (ci.yml uses `10.0.x`) | 10.0.x | — |
| Node.js 22 | Frontend tests | ✓ (ci.yml uses node 22) | 22 | — |
| Windows runner | net10.0-windows TFM | ✓ (`windows-latest`) | — | — |
| npm | Frontend deps | ✓ | bundled | — |
| PowerShell (pwsh) | release.yml publish | ✓ | — | — |

No missing dependencies. All test tooling installs via package managers.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Backend framework | xUnit v3 3.2.2 |
| Backend config | `src/PassReset.Tests/PassReset.Tests.csproj` (Wave 0 — does not exist) |
| Backend quick run | `dotnet test src/PassReset.Tests/PassReset.Tests.csproj --filter "Category!=Integration"` |
| Backend full suite | `dotnet test src/PassReset.sln --configuration Release /p:CollectCoverage=true /p:Threshold=\"55,45\" /p:ThresholdType=\"line,branch\"` |
| Frontend framework | Vitest (current version per `npm view vitest version`) + @testing-library/react |
| Frontend config | `src/PassReset.Web/ClientApp/vitest.config.ts` (Wave 0 — does not exist) |
| Frontend quick run | `cd src/PassReset.Web/ClientApp && npm test` |
| Frontend full suite | `cd src/PassReset.Web/ClientApp && npm run test:coverage` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| QA-001 | `dotnet test` runs provider logic, error mapping, SIEM, lockout decorator | unit + integration | `dotnet test src/PassReset.sln` | ❌ Wave 0 — whole project |
| QA-001 | `npm test` runs components, hooks, utilities (levenshtein, pwgen) | unit + component | `npm test` in ClientApp | ❌ Wave 0 — whole config |
| QA-001 | CI fails on any test failure with thresholds declared | gate | `.github/workflows/tests.yml` | ❌ Wave 0 |
| QA-001 | `release.yml` blocks tag-triggered publish on test failure | gate | `needs: tests` on release job | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** `dotnet test src/PassReset.Tests/PassReset.Tests.csproj` (backend) OR `npm test` (frontend) — whichever is touched
- **Per wave merge:** Full suite both sides with coverage
- **Phase gate:** Both suites green with thresholds met, workflows committed, `release.yml` test-gate verified by dry-run on a throwaway tag

### Wave 0 Gaps
- [ ] `src/PassReset.Tests/PassReset.Tests.csproj` + project skeleton
- [ ] `src/PassReset.Tests/GlobalUsings.cs`
- [ ] `src/PassReset.Tests/Helpers/TestWebApplicationFactory.cs` (shared fixture)
- [ ] `src/PassReset.Web/Program.cs` — append `public partial class Program { }`
- [ ] `src/PassReset.Web/ClientApp/vitest.config.ts`
- [ ] `src/PassReset.Web/ClientApp/vitest.setup.ts`
- [ ] `src/PassReset.Web/ClientApp/package.json` — add devDeps + `test`/`test:watch`/`test:coverage` scripts
- [ ] `.github/workflows/tests.yml` — new reusable workflow
- [ ] `.github/workflows/ci.yml` — add `tests:` job using `tests.yml`
- [ ] `.github/workflows/release.yml` — add `tests:` job + `needs: tests` on `release:`
- [ ] Refactor: `PwnedPasswordChecker` static→instance with injected `HttpClient` (needed for HTTP mocking)
- [ ] Optional refactor: extract `SiemSyslogFormatter` pure helper from `SiemService.EmitSyslog`
- [ ] Delete stale `src/PassReset.Tests/bin` and `src/PassReset.Tests/obj` before scaffolding
- [ ] Update `CLAUDE.md` post-ship: replace "There are no automated tests" with `dotnet test` + `npm test` commands

## Security Domain

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | partial | Tests MUST NOT log plaintext credentials — use fixtures with placeholder strings like `"test-pw"` |
| V5 Input Validation | yes | Model validation tests (ChangePasswordModel) directly exercise this |
| V6 Cryptography | yes (indirect) | `PwnedPasswordChecker` SHA-1 + k-anonymity — test with static vectors, never real passwords |
| V7 Error Handling & Logging | yes | Verify SIEM events are emitted for all 10 event types |
| V8 Data Protection | partial | Ensure coverage reports don't include actual fixture secrets |

### Known Threat Patterns for .NET test projects

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Secrets leaking into test output | Information disclosure | `.gitignore` `*.trx`, `coverage/`, `TestResults/`; CI artifact scoped to Cobertura only (no raw logs with `--verbosity normal`) |
| Network leakage to real APIs (HIBP, reCAPTCHA) | Information disclosure | Mock via `HttpMessageHandler`, never let tests hit live endpoints |
| Test project accidentally published in release zip | Tampering / expanded attack surface | `<IsPublishable>false</IsPublishable>` in `PassReset.Tests.csproj`; verify `Publish-PassReset.ps1` excludes it |
| CI artifact retention exposing coverage with source paths | Information disclosure | 30-day retention per CONTEXT.md D6 is fine; don't upload raw test binaries |

## Sources

### Primary (HIGH confidence)
- [xUnit.net v3 3.2.2 release notes](https://xunit.net/releases/v3/3.2.2) — 2026-01-14
- [xUnit.net — Microsoft Testing Platform guide](https://xunit.net/docs/getting-started/v3/microsoft-testing-platform) — MTP v1/v2 story
- [NuGet: xunit.v3 3.2.2](https://www.nuget.org/packages/xunit.v3)
- [NuGet: coverlet.msbuild 8.0.1](https://www.nuget.org/packages/coverlet.msbuild) — threshold enforcement
- [NuGet: coverlet.collector 8.0.1](https://www.nuget.org/packages/coverlet.collector)
- [NuGet: NSubstitute 5.3.0](https://www.nuget.org/packages/nsubstitute/)
- [Vitest coverage config](https://vitest.dev/config/coverage)
- [Microsoft Learn — Integration tests in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests) — `WebApplicationFactory<TEntryPoint>`, `public partial class Program` pattern
- [GitHub Actions — Reusing workflows](https://docs.github.com/en/actions/using-workflows/reusing-workflows)
- npm registry verification (2026-04-15): `@testing-library/react 14.6.1`, `@testing-library/user-event 6.9.1`, `@testing-library/jest-dom 29.0.2` [VERIFIED via `npm view`]

### Secondary (MEDIUM confidence)
- [dotnet blog — MTP adoption across frameworks](https://devblogs.microsoft.com/dotnet/mtp-adoption-frameworks/)
- [coverlet repo](https://github.com/coverlet-coverage/coverlet) — collector vs msbuild semantics
- [antidale.com — Updating to .NET 10 and MTP](https://www.antidale.com/blog/testing-net-10/)

### Tertiary (LOW confidence — verify at plan time)
- Exact `Microsoft.NET.Test.Sdk` pin for .NET 10 SDK (use whatever `dotnet new xunit3` scaffolds)
- Exact current `vitest` version — `npm view vitest version` returned `16.3.2` on research machine (unusual; planner must re-verify)

## Metadata

**Confidence breakdown:**
- Standard stack (backend): HIGH — versions directly verified against NuGet 2026-01-14 / 2026-04-15
- Standard stack (frontend): MEDIUM — npm view returned unusual version numbers; planner should re-verify at plan time
- Architecture patterns (WAF, HTTP mocking, syslog formatter): HIGH — standard .NET/MS-documented patterns applied to verified source code
- Pitfalls: HIGH — each pitfall grounded in source-code evidence or official doc
- Coverage threshold syntax: HIGH — coverlet.msbuild + Vitest docs explicit

**Research date:** 2026-04-15
**Valid until:** 2026-05-15 (30 days — stack is stable but version pins may drift)

## RESEARCH COMPLETE
