# Phase 3: v1.3 UX Features — Research

**Researched:** 2026-04-15
**Domain:** ASP.NET Core 10 + React 19 / MUI 6 — branding, AD policy query, clipboard safety, HIBP blur check
**Confidence:** HIGH (codebase patterns verified by direct read; no new libraries introduced)

## Summary

All four features extend existing patterns already in the codebase — no new NuGet/npm dependencies required. The heaviest lift is FEAT-002 (RootDSE password-policy query + new endpoint + `IPasswordChangeProvider` extension). FEAT-001 is pure plumbing (config shape, `PhysicalFileProvider`, React components). FEAT-003 is a browser-only timer with readback guard. FEAT-004 reuses the existing `PwnedPasswordChecker` behind a new proxy endpoint plus a WebCrypto SHA-1 helper.

**Primary recommendation:** Mirror the existing `password-fixed-window` rate-limit + `PasswordController` pattern for both new endpoints; extend `IPasswordChangeProvider` with `GetEffectivePasswordPolicyAsync` following the `GetDomainMaxPasswordAge` template (uses `AcquireDomainEntry()` which already does `Domain.GetCurrentDomain().GetDirectoryEntry()` or explicit LDAP bind).

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

**FEAT-001 — Branding**
- Asset storage path: `C:\ProgramData\PassReset\brand\` (outside app install dir). `Install-PassReset.ps1` must never touch this folder on upgrade. Default path is used when `ClientSettings.Branding.AssetRoot` is null.
- Static serving: ASP.NET Core serves `/brand/*` from the asset root via `PhysicalFileProvider` + `StaticFileOptions` mapped in `Program.cs`. Path configurable via `ClientSettings.Branding.AssetRoot`.
- Config shape: Nested `ClientSettings.Branding { CompanyName, PortalName, HelpdeskUrl, HelpdeskEmail, UsageText, LogoFileName, FaviconFileName, AssetRoot }`. When null → current v1.2.3 default look.
- Helpdesk rendering: URL `<a target="_blank" rel="noopener">`, email `<a href="mailto:">`. Block only if at least one field configured.
- Favicon: `<link rel="icon">` href injected at runtime from `Branding.FaviconFileName`.

**FEAT-002 — AD Policy Panel**
- Data source: RootDSE — `minPwdLength`, `pwdProperties`, `pwdHistoryLength`, `minPwdAge`, `maxPwdAge` on domain NC head. Fine-grained PSOs deferred.
- Cache: in-memory TTL 1h (success), 60s (failure). Key = domain DN.
- Failure mode: panel renders nothing (fails closed).
- Endpoint: `GET /api/password/policy` → `{ minLength, requiresComplexity, historyLength, minAgeDays, maxAgeDays }` or 404/empty when unavailable.
- Interface: add `GetEffectivePasswordPolicyAsync()` to `IPasswordChangeProvider`; implement on real + debug providers.

**FEAT-003 — Clipboard Clearing**
- Trigger: generator copy → timer `ClipboardClearSeconds` (default 30, 0 disables).
- Safety: `navigator.clipboard.readText()` compare, only `writeText('')` if match.
- Permission prompt accepted (Firefox/Safari).
- Regenerate cancels previous timer and updates tracked value.
- No-op when API unavailable or setting = 0.

**FEAT-004 — HIBP Blur Indicator**
- New endpoint `POST /api/password/pwned-check`; client computes SHA-1 via WebCrypto, sends 5-char hex prefix.
- Plaintext never leaves client.
- Debounce 400 ms; abort in-flight on value change.
- Fail-open honors `FailOpenOnPwnedCheckUnavailable`.
- Rate limit: 20 req / 5 min per IP (new named policy).
- SIEM: only rate-limiter rejections + unavailable-downgrade logged (not per-request).

### Claude's Discretion
- MUI component choices inside each new component (UI-SPEC contracts remain authoritative).
- DI wiring of AD policy cache: `IMemoryCache` vs. tiny custom TTL dict.
- Exact SHA-1 helper location (`utils/sha1.ts` default).
- Internal `components/` sub-folder layout (may introduce `branding/`).
- Test layout (owned by Phase 2 conventions).

### Deferred Ideas (OUT OF SCOPE)
- Fine-grained Password Settings Objects (PSO) resolution — v2.0.
- Operator branding upload UI — future phase (operator drops files manually).
- CSP-strict HIBP direct-from-browser — rejected; keep server hop.
- Clipboard-clear without read permission — rejected (needs readback guarantee).
- Multi-language / i18n branding — not in v1.3.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| FEAT-001 | Operator branding via `ClientSettings.Branding`, upgrade-safe asset path, defaults preserve look | §FEAT-001 below — `PhysicalFileProvider` wiring, config shape, favicon swap pattern |
| FEAT-002 | Optional AD policy panel above new-password, gated on `ShowAdPasswordPolicy`, hidden on AD failure | §FEAT-002 — RootDSE query via existing `AcquireDomainEntry()`, new endpoint, `IMemoryCache` |
| FEAT-003 | Clipboard clear after `ClipboardClearSeconds` only if clipboard still matches generated value | §FEAT-003 — `navigator.clipboard` feature check + readback guard, timer lifecycle |
| FEAT-004 | HIBP blur indicator — debounced, fail-open aware, plaintext stays on client | §FEAT-004 — new endpoint reusing `PwnedPasswordChecker`, WebCrypto SHA-1, AbortController |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- **Windows-only** — `net10.0-windows` for `PassReset.PasswordProvider` and `PassReset.Web`; `PassReset.Common` stays platform-neutral (`net10.0`). Do not add `System.DirectoryServices` references to `Common`.
- **No breaking config changes** — pre-v1.3 `appsettings.Production.json` must continue to work; every new `ClientSettings` key has a safe default.
- **Commit convention**: `type(scope): subject`. Scopes: `web | provider | common | deploy | docs | ci | deps | security | installer`.
- **No automated tests yet** — Phase 2 delivers xUnit + Vitest. Phase 3 should write test stubs/hooks that Phase 2 can pick up but cannot assume tests run.
- **Secrets stay `[JsonIgnore]`** — no branding secret is exposed to client; all `Branding.*` fields are safe to serialize.
- **Provider pattern** — `IPasswordChangeProvider` is THE contract; new capabilities added there with stubs in `DebugPasswordChangeProvider`.
- **Controller pattern** — one controller file per route group (`PasswordController`). Add new endpoints to existing controller rather than new one for `/api/password/*`.
- **GSD workflow** — file edits gated on `/gsd:execute-phase` after plans approved.

## Standard Stack

### Core (unchanged from v1.2.3)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| ASP.NET Core | 10.0 | Web API host | [VERIFIED: project csproj] |
| React | 19.1.0 | SPA | [VERIFIED: package.json] |
| MUI | 6.4.8 | Components + theming | [VERIFIED: package.json] |
| System.DirectoryServices.AccountManagement | 10.0.5 | AD bind | [VERIFIED] — existing provider uses it |
| `Microsoft.Extensions.Caching.Memory` | 10.0.x (transitive via AspNetCore) | Policy TTL cache | [VERIFIED] — in ASP.NET Core shared framework |

### Supporting
| API | Purpose | Notes |
|-----|---------|-------|
| `Microsoft.Extensions.FileProviders.PhysicalFileProvider` | Serve `/brand/*` from `C:\ProgramData\PassReset\brand\` | Already in ASP.NET Core; no new NuGet |
| `Microsoft.AspNetCore.RateLimiting` | Second named policy for pwned-check | Already in use — `password-fixed-window` in `Program.cs:137` |
| `window.crypto.subtle.digest('SHA-1', …)` | Client hash | Built-in to every evergreen browser [VERIFIED: MDN] |
| `AbortController` | Cancel in-flight HIBP fetch | Native fetch, already supported |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| `IMemoryCache` | Custom `ConcurrentDictionary<string, (DateTime expiry, PolicyDto value)>` | Custom is simpler, fewer layers; `IMemoryCache` is idiomatic, supports eviction callbacks. **Recommend `IMemoryCache`** — smaller delta, testable. |
| Server SHA-1 hop | Direct browser → HIBP | Rejected in CONTEXT.md (CSP/FailOpen + SIEM reasons). |
| react-helmet-async for favicon | Direct DOM `useEffect` | No dependency; codebase has no helmet today. **Recommend useEffect.** |

**Installation:** None. All additions use existing references.

## Architecture Patterns

### Recommended Project Structure
```
src/PassReset.Common/
  IPasswordChangeProvider.cs        # add GetEffectivePasswordPolicyAsync
  PasswordPolicy.cs                 # NEW — DTO record (platform-neutral)

src/PassReset.PasswordProvider/
  PasswordChangeProvider.cs         # implement GetEffectivePasswordPolicyAsync (RootDSE)
  PasswordPolicyCache.cs            # NEW — IMemoryCache wrapper (1h / 60s TTL)

src/PassReset.Web/
  Controllers/PasswordController.cs # ADD GET /policy + POST /pwned-check
  Models/ClientSettings.cs          # ADD Branding nested class
  Helpers/DebugPasswordChangeProvider.cs  # stub GetEffectivePasswordPolicyAsync

src/PassReset.Web/ClientApp/src/
  components/
    BrandHeader.tsx                 # NEW
    AdPasswordPolicyPanel.tsx       # NEW
    HibpIndicator.tsx               # NEW
    ClipboardCountdown.tsx          # NEW
  hooks/
    useSettings.ts                  # extend — Branding surface + favicon effect
    usePolicy.ts                    # NEW — GET /api/password/policy
    useHibpCheck.ts                 # NEW — debounce + abort
  api/client.ts                     # add fetchPolicy, postPwnedCheck
  types/settings.ts                 # add Branding, PolicyResponse, PwnedCheckResponse
  utils/
    sha1.ts                         # NEW — WebCrypto hex
    clipboardClear.ts               # NEW — timer + readback guard
```

### Pattern 1: Extending `IPasswordChangeProvider`
**When:** any new AD-dependent capability.
**Rule:** default interface methods not used here — both providers implement explicitly.
**Example (existing):**
```csharp
// src/PassReset.Common/IPasswordChangeProvider.cs:33
TimeSpan GetDomainMaxPasswordAge();
// src/PassReset.PasswordProvider/PasswordChangeProvider.cs:209
public TimeSpan GetDomainMaxPasswordAge()
{
    using var entry = AcquireDomainEntry();
    var rawValue = entry.Properties["maxPwdAge"].Value;
    // …interval math…
}
// AcquireDomainEntry at line 504:
//   UseAutomaticContext=true → Domain.GetCurrentDomain().GetDirectoryEntry()
//   else → new DirectoryEntry($"{LDAP|LDAPS}://{host}:{port}", user, pass, authType)
```
Apply the exact same pattern for policy fields: bind domain entry, read `minPwdLength`, `pwdProperties` (bitmask — `DOMAIN_PASSWORD_COMPLEX = 0x1`), `pwdHistoryLength`, `minPwdAge`, `maxPwdAge` from the same `entry.Properties`.

### Pattern 2: `PhysicalFileProvider` for branding assets
**Where:** `Program.cs` — AFTER `app.UseStaticFiles()` (default wwwroot) and BEFORE `app.UseRouting()` at line 203.
```csharp
// Default if Branding.AssetRoot null
var brandRoot = clientSettings.Branding?.AssetRoot
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "PassReset", "brand");
Directory.CreateDirectory(brandRoot); // defensive — safe on existing dir

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(brandRoot),
    RequestPath = "/brand",
    ServeUnknownFileTypes = false
});
```
**Gotcha:** `PhysicalFileProvider` constructor throws if path doesn't exist. `Directory.CreateDirectory` is idempotent and safe.

### Pattern 3: Second named rate-limit policy
**Existing at `Program.cs:149`:**
```csharp
options.AddPolicy("password-fixed-window", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(5),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0 }));
```
**Add alongside:**
```csharp
options.AddPolicy("pwned-check-window", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(5),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0 }));
```
**Attach to controller action:** `[EnableRateLimiting("pwned-check-window")]` — sibling of existing `[EnableRateLimiting("password-fixed-window")]` at `PasswordController.cs:66`.

### Pattern 4: `IMemoryCache` for policy TTL
`services.AddMemoryCache()` is **NOT** registered by default in `CreateBuilder`. Must add:
```csharp
builder.Services.AddMemoryCache();
```
Then inject `IMemoryCache _cache` and use:
```csharp
if (_cache.TryGetValue("policy", out PasswordPolicy? cached)) return cached;
try {
    var policy = QueryFromAd();
    _cache.Set("policy", policy, TimeSpan.FromHours(1));
    return policy;
} catch {
    _cache.Set<PasswordPolicy?>("policy", null, TimeSpan.FromSeconds(60));
    return null;
}
```

### Pattern 5: Controller endpoint signature
**Existing `PasswordController.cs:65`:**
```csharp
[HttpPost]
[EnableRateLimiting("password-fixed-window")]
[RequestSizeLimit(8192)]
public async Task<IActionResult> PostAsync([FromBody] ChangePasswordModel model) { … }
```
**New siblings:**
```csharp
[HttpGet("policy")]
public async Task<IActionResult> GetPolicyAsync()
{
    if (!_settings.ShowAdPasswordPolicy) return NotFound();
    var policy = await _provider.GetEffectivePasswordPolicyAsync();
    return policy is null ? NotFound() : Ok(policy);
}

[HttpPost("pwned-check")]
[EnableRateLimiting("pwned-check-window")]
[RequestSizeLimit(64)]
public async Task<IActionResult> PwnedCheckAsync([FromBody] PwnedCheckRequest req) { … }
```

### Anti-Patterns to Avoid
- **Directly embedding logo bytes in `ClientSettings`** — would bloat every `/api/password` response and require base64. Use static-file URL.
- **Querying AD on every `/api/password/policy` request** — must hit cache; AD calls are slow and DCs shouldn't be hammered.
- **Sending full SHA-1 to server** — violates k-anonymity contract. Send 5-char prefix only.
- **Synchronously awaiting `readText()` inside `setTimeout` without permission-denied catch** — will throw in Safari/Firefox if user denies. Wrap in try/catch; fall back to unconditional `writeText('')` only if policy allows (see UI-SPEC FEAT-003 "clear unconditionally after countdown" fallback).
- **Using `react-helmet-async`** — adds a new dep for a single `<link>` swap. Use `useEffect` + direct DOM.
- **Logging every HIBP lookup to SIEM** — CONTEXT.md explicitly rejects this (too noisy).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| SHA-1 in browser | JS SHA-1 lib (crypto-js) | `crypto.subtle.digest('SHA-1', …)` | Native, faster, no bundle cost. [CITED: MDN SubtleCrypto] |
| TTL cache | Custom dict + timer | `IMemoryCache` | Official ASP.NET Core primitive, supports sliding/absolute expiry |
| HIBP k-anonymity client | Reinvent range API | Reuse `PwnedPasswordChecker.IsPwnedPasswordAsync` | Already production-hardened |
| Rate limiting | Third-party (AspNetCoreRateLimit) | `Microsoft.AspNetCore.RateLimiting` | Already in use, consistent with existing policy |
| Favicon management | react-helmet-async | `useEffect` + `document.querySelector('link[rel=icon]')` | Single element, no dep cost |
| Debounce | lodash.debounce | 10-line `useEffect`+`setTimeout` hook (repo pattern — see `PasswordStrengthMeter` 250ms debounce) | Consistent with existing code |
| Fetch abort | axios `CancelToken` | Native `AbortController` | fetch is already project standard (see `api/client.ts:4`) |
| pwdProperties bitmask parsing | Re-define flags | Use Microsoft-documented values: `DOMAIN_PASSWORD_COMPLEX=0x1`, `DOMAIN_PASSWORD_NO_ANON_CHANGE=0x2`, `DOMAIN_PASSWORD_NO_CLEAR_CHANGE=0x4`, `DOMAIN_LOCKOUT_ADMINS=0x8`, `DOMAIN_PASSWORD_STORE_CLEARTEXT=0x10`, `DOMAIN_REFUSE_PASSWORD_CHANGE=0x20` [CITED: docs.microsoft.com] | Only need `0x1` for the panel |

**Key insight:** every capability needed by this phase is already expressed elsewhere in the codebase or in the framework. The job is wiring, not invention.

## Runtime State Inventory

Not applicable — this is an additive feature phase (no rename/refactor/migration).

| Category | Findings |
|----------|----------|
| Stored data | None — no schema or DB changes |
| Live service config | `C:\ProgramData\PassReset\brand\` must exist at runtime but is out-of-git by design (operator assets). Installer creates if missing, never overwrites. |
| OS-registered state | IIS AppPool unchanged |
| Secrets/env vars | None — no new secrets |
| Build artifacts | Frontend bundle changes; re-publish required on upgrade |

## Common Pitfalls

### Pitfall 1: `PhysicalFileProvider` path does not exist at startup
**What goes wrong:** `DirectoryNotFoundException` on app start if `C:\ProgramData\PassReset\brand\` doesn't exist.
**Why:** Installer creates it, but dev envs or hand-deployed servers may not.
**How to avoid:** `Directory.CreateDirectory(brandRoot)` before constructing the provider (idempotent).
**Warning signs:** App fails to start after fresh install on a machine where installer was skipped.

### Pitfall 2: `maxPwdAge` / `minPwdAge` stored as negative 100-ns FILETIME interval
**What goes wrong:** Returning raw `long` gives negative seconds-squared nonsense.
**Why:** AD stores these as `Int64` negative 100-nanosecond intervals; 0 = no policy.
**How to avoid:** `TimeSpan.FromTicks(-rawValue)` conversion — existing `GetDomainMaxPasswordAge` already does this at `PasswordChangeProvider.cs:209+`; copy its math.
**Warning signs:** Reported "max age: -922337203... days" in UI.

### Pitfall 3: `navigator.clipboard.readText()` throws in user-gesture-less contexts
**What goes wrong:** Silent permission denial or `DOMException`.
**Why:** Chrome requires user-gesture; some browsers block `readText` in iframes or without explicit permission.
**How to avoid:** Wrap in try/catch; on denial, fall back to unconditional `writeText('')` (UI-SPEC §FEAT-003 sanctions this fallback). Feature-detect with `'clipboard' in navigator && 'readText' in navigator.clipboard` before binding the timer.
**Warning signs:** Clipboard stays full after countdown in Firefox.

### Pitfall 4: AbortController leaked when component unmounts mid-request
**What goes wrong:** Setting React state on unmounted component → warning + memory leak.
**Why:** HIBP fetch slow → user navigates → setState fires after unmount.
**How to avoid:** Store `controller.abort()` in `useEffect` cleanup; check `signal.aborted` after `await` before setState.

### Pitfall 5: CSP blocks `/brand/*` images
**What goes wrong:** Logos fail to load; console "Refused to load image" warnings.
**Why:** CSP `img-src` directive.
**Prevention:** Current CSP at `Program.cs:183` is `img-src 'self' data:`. Since `/brand/*` IS `'self'` (same origin), images load without CSP change. **Verified — no CSP update needed.**
**Also confirmed:** `connect-src 'self'` at `Program.cs:184` — HIBP endpoint is same-origin, so no change.

### Pitfall 6: `FailOpenOnPwnedCheckUnavailable` semantics split between client + server
**What goes wrong:** Mismatch — server returns 503 but client renders success (or vice-versa).
**Why:** The same flag governs both the existing submit-path behavior AND the new blur-path.
**How to avoid:** Server owns the decision. When HIBP fails:
- `FailOpenOnPwnedCheckUnavailable=true` → 200 `{ breached: false, unavailable: true }`
- `FailOpenOnPwnedCheckUnavailable=false` → 503 (no body needed)
Client always trusts response.

### Pitfall 7: Backward-compat break when `ClientSettings.Branding` binding null-coalesces wrong
**What goes wrong:** Absent config section → `Branding` is null → NRE in controller.
**How to avoid:** All new properties nullable (`Branding? Branding { get; set; }`), defaults materialized in `PasswordController.GetAsync` or in the view layer. TypeScript side mirrors with `branding?: Branding | null`.

## Code Examples

### Server: RootDSE policy read (C#)
Template based on `PasswordChangeProvider.cs:209` (`GetDomainMaxPasswordAge`) and `:504` (`AcquireDomainEntry`):

```csharp
// Source: existing GetDomainMaxPasswordAge pattern
public async Task<PasswordPolicy?> GetEffectivePasswordPolicyAsync()
{
    return await Task.Run(() =>
    {
        try
        {
            using var entry = AcquireDomainEntry();
            var minLength   = (int)(entry.Properties["minPwdLength"].Value ?? 0);
            var flags       = (int)(entry.Properties["pwdProperties"].Value ?? 0);
            var history     = (int)(entry.Properties["pwdHistoryLength"].Value ?? 0);
            // AD stores ages as negative 100-ns intervals (Int64)
            var maxAgeRaw   = (long?)entry.Properties["maxPwdAge"].Value ?? 0;
            var minAgeRaw   = (long?)entry.Properties["minPwdAge"].Value ?? 0;

            const int DOMAIN_PASSWORD_COMPLEX = 0x1;

            return new PasswordPolicy(
                MinLength:          minLength,
                RequiresComplexity: (flags & DOMAIN_PASSWORD_COMPLEX) != 0,
                HistoryLength:      history,
                MinAgeDays:         minAgeRaw == 0 ? 0 : (int)TimeSpan.FromTicks(-minAgeRaw).TotalDays,
                MaxAgeDays:         maxAgeRaw == 0 ? 0 : (int)TimeSpan.FromTicks(-maxAgeRaw).TotalDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read default domain password policy from RootDSE");
            return (PasswordPolicy?)null;
        }
    });
}
```

### Server: HIBP proxy endpoint (C#)
```csharp
public record PwnedCheckRequest(string Prefix);
public record PwnedCheckResponse(bool Breached, int Count, bool Unavailable);

[HttpPost("pwned-check")]
[EnableRateLimiting("pwned-check-window")]
[RequestSizeLimit(64)]
public async Task<IActionResult> PwnedCheckAsync([FromBody] PwnedCheckRequest req)
{
    if (string.IsNullOrWhiteSpace(req.Prefix) || req.Prefix.Length != 5
        || !req.Prefix.All(Uri.IsHexDigit))
        return BadRequest();

    // PwnedPasswordChecker is internal static today — either add a
    // prefix-range method or add [InternalsVisibleTo("PassReset.Web")]
    var (breached, count, unavailable) =
        await _pwnedChecker.CheckByPrefixAsync(req.Prefix.ToUpperInvariant());

    if (unavailable && !_options.FailOpenOnPwnedCheckUnavailable)
        return StatusCode(503);

    return Ok(new PwnedCheckResponse(breached, count, unavailable));
}
```

### Client: SHA-1 helper
```ts
// utils/sha1.ts
export async function sha1HexPrefix(password: string, len = 5): Promise<string> {
  const bytes   = new TextEncoder().encode(password.normalize('NFC'));
  const digest  = await crypto.subtle.digest('SHA-1', bytes);
  const hex     = [...new Uint8Array(digest)]
                    .map(b => b.toString(16).padStart(2, '0'))
                    .join('')
                    .toUpperCase();
  return hex.slice(0, len);
}
```
**Unicode note:** Always `normalize('NFC')` before hashing — HIBP precomputed hashes assume NFC.

### Client: debounced HIBP hook with abort
```ts
// hooks/useHibpCheck.ts
export function useHibpCheck(password: string, enabled: boolean) {
  const [state, setState] = useState<'idle'|'checking'|'safe'|'breached'|'unavailable'|'error'>('idle');
  const [count, setCount] = useState(0);

  useEffect(() => {
    if (!enabled || !password) { setState('idle'); return; }
    const ctrl = new AbortController();
    const t = setTimeout(async () => {
      setState('checking');
      try {
        const prefix = await sha1HexPrefix(password);
        const res = await fetch('/api/password/pwned-check', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ prefix }),
          signal: ctrl.signal,
        });
        if (ctrl.signal.aborted) return;
        if (res.status === 503) { setState('error'); return; }
        const data = await res.json() as PwnedCheckResponse;
        if (data.unavailable) setState('unavailable');
        else if (data.breached) { setCount(data.count); setState('breached'); }
        else setState('safe');
      } catch (e) {
        if (!ctrl.signal.aborted) setState('error');
      }
    }, 400);
    return () => { clearTimeout(t); ctrl.abort(); };
  }, [password, enabled]);

  return { state, count };
}
```

### Client: clipboard clear with readback
```ts
// utils/clipboardClear.ts
export function scheduleClipboardClear(generated: string, seconds: number) {
  if (seconds <= 0) return () => {};
  if (!('clipboard' in navigator) || typeof navigator.clipboard.writeText !== 'function')
    return () => {};

  const id = setTimeout(async () => {
    try {
      const current = typeof navigator.clipboard.readText === 'function'
        ? await navigator.clipboard.readText() : null;
      if (current === null || current === generated)
        await navigator.clipboard.writeText('');
    } catch { /* permission denied — silent */ }
  }, seconds * 1000);

  return () => clearTimeout(id);
}
```

### Client: favicon swap
```ts
// inside useSettings or a small effect in App.tsx
useEffect(() => {
  const href = branding?.faviconFileName
    ? `/brand/${branding.faviconFileName}`
    : '/favicon.ico';
  let link = document.querySelector<HTMLLinkElement>('link[rel="icon"]');
  if (!link) {
    link = document.createElement('link');
    link.rel = 'icon';
    document.head.appendChild(link);
  }
  link.href = href;
}, [branding?.faviconFileName]);
```

### Installer: create-if-missing idiom
Existing pattern in `Install-PassReset.ps1` (e.g. lines 181–183, 438):
```powershell
$brandRoot = Join-Path $env:ProgramData 'PassReset\brand'
if (-not (Test-Path $brandRoot)) {
    New-Item -ItemType Directory -Path $brandRoot -Force | Out-Null
    Write-Ok "Created $brandRoot"
} else {
    Write-Info "$brandRoot exists — preserving operator assets"
}
# NEVER Remove-Item $brandRoot on upgrade — operator owns it
```
Follows the same shape as lines 181 (`$PhysicalPath`), 438 (`$logsPath`), and 458–459 (`appsettings.Production.json` preserve pattern).

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `UseFileServer` bundle | `UseStaticFiles + PhysicalFileProvider + RequestPath` | ASP.NET Core 3.x | Cleaner segregation of brand assets |
| `Microsoft.AspNetCore.Mvc.JsonOptions` manual parse | System.Text.Json default + record types | .NET 5+ | Records give free equality/immutability for DTOs |
| `XMLHttpRequest` | `fetch` + `AbortController` | Baseline in all evergreen browsers | Project already standardized (`api/client.ts`) |
| crypto-js / JS SHA-1 | `crypto.subtle.digest` | Baseline everywhere since ~2018 | Zero bundle cost |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `PwnedPasswordChecker` currently hashes plaintext and queries HIBP range — needs either new `CheckByPrefixAsync(prefix)` method or `InternalsVisibleTo` exposure | §FEAT-004, Code example | If the internal API differs, the prefix flow needs a small refactor. Plan task must verify and extract the range-query primitive. |
| A2 | `Microsoft.Extensions.Caching.Memory` is available transitively | §IMemoryCache | If not, add explicit `<PackageReference>`. Low risk — it's in `Microsoft.AspNetCore.App` shared framework. |
| A3 | `pwdProperties` flag names/values [CITED: Microsoft docs] are stable | §Don't Hand-Roll | Values are standardized Windows constants — unchanged since Windows 2000. |
| A4 | `Install-PassReset.ps1` upgrade path does not currently touch `C:\ProgramData\*` | §Installer | Grep confirms no `ProgramData` references exist today — safe. |

## Open Questions

1. **PwnedPasswordChecker exposure** — currently `internal static`. Plan must decide: (a) expose as instance service via `IPwnedPasswordChecker`, (b) add `InternalsVisibleTo`, or (c) add a second static method `CheckByPrefixAsync(string prefix)`. Recommendation: (a) — aligns with DI pattern of all other services.
2. **Should `GET /api/password/policy` be under rate limit?** — CONTEXT.md doesn't specify. Called once on mount; adding existing `password-fixed-window` is cheap insurance.
3. **Debug provider policy fixture** — what values should `DebugPasswordChangeProvider.GetEffectivePasswordPolicyAsync` return? Suggest: `MinLength=8, RequiresComplexity=true, HistoryLength=5, MinAgeDays=1, MaxAgeDays=60` to exercise the UI.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET 10 SDK | Build | ✓ | 10.0 | — |
| Node 20+ / npm | Frontend build | ✓ | per package.json engines | — |
| IIS + ASP.NET Core module | Runtime | ✓ | — | — |
| Active Directory | FEAT-002 runtime | Env-dependent | — | Panel hides (fail-closed) |
| `api.pwnedpasswords.com` | FEAT-004 runtime | Public | — | `FailOpenOnPwnedCheckUnavailable` flag controls |
| `C:\ProgramData` writable | FEAT-001 runtime | Windows default | — | `ClientSettings.Branding.AssetRoot` override |

All blocking. No dev-env gaps.

## Validation Architecture

Nyquist enabled per `.planning/config.json` defaults. Phase 2 (parallel) delivers the actual test framework; Phase 3 must produce verifiable artifacts that Phase 2's harness can exercise.

### Test Framework
| Property | Value |
|----------|-------|
| Framework (backend) | xUnit v3 — arrives in Phase 2 (plan 02-01) |
| Framework (frontend) | Vitest + RTL + jsdom — arrives in Phase 2 (plan 02-03) |
| Config file (backend) | `src/PassReset.Tests/PassReset.Tests.csproj` (Phase 2) |
| Config file (frontend) | `ClientApp/vitest.config.ts` (Phase 2) |
| Quick run command | `dotnet test src/PassReset.sln --filter Category!=Integration` / `npm test` |
| Full suite command | `dotnet test src/PassReset.sln` / `npm test -- --run` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|--------------|
| FEAT-001 | `Branding` null → current default render | unit (RTL) | `npm test -- BrandHeader` | ❌ Wave 0 |
| FEAT-001 | Logo URL rendered when configured | unit (RTL) | `npm test -- BrandHeader` | ❌ Wave 0 |
| FEAT-001 | `/brand/*` returns static files from configured root | integration (WebApplicationFactory) | `dotnet test --filter Brand` | ❌ Wave 0 |
| FEAT-001 | Missing brand dir auto-created at startup | integration | `dotnet test --filter Brand` | ❌ Wave 0 |
| FEAT-002 | Panel hidden when `ShowAdPasswordPolicy=false` | unit (RTL) | `npm test -- AdPasswordPolicyPanel` | ❌ Wave 0 |
| FEAT-002 | Panel hidden on 404 | unit (RTL) | `npm test -- AdPasswordPolicyPanel` | ❌ Wave 0 |
| FEAT-002 | `GetEffectivePasswordPolicyAsync` returns null on `DirectoryServicesCOMException` | unit (xUnit) | `dotnet test --filter Policy` | ❌ Wave 0 |
| FEAT-002 | `PasswordPolicyCache` honors 1h TTL on success | unit (xUnit) | `dotnet test --filter PolicyCache` | ❌ Wave 0 |
| FEAT-003 | `ClipboardClearSeconds=0` never starts timer | unit (Vitest) | `npm test -- clipboardClear` | ❌ Wave 0 |
| FEAT-003 | Timer clears only when readback matches generated | unit (Vitest, mocked clipboard) | `npm test -- clipboardClear` | ❌ Wave 0 |
| FEAT-003 | readText throws → silent no-op | unit (Vitest) | `npm test -- clipboardClear` | ❌ Wave 0 |
| FEAT-004 | SHA-1 prefix computed client-side (plaintext never in request) | unit (Vitest) | `npm test -- sha1` | ❌ Wave 0 |
| FEAT-004 | 400ms debounce — rapid blur fires one request | unit (RTL + fake timers) | `npm test -- useHibpCheck` | ❌ Wave 0 |
| FEAT-004 | AbortController cancels previous in-flight | unit (Vitest) | `npm test -- useHibpCheck` | ❌ Wave 0 |
| FEAT-004 | Fail-open=true + HIBP fail → 200 unavailable | unit (xUnit) | `dotnet test --filter PwnedCheck` | ❌ Wave 0 |
| FEAT-004 | Fail-open=false + HIBP fail → 503 | unit (xUnit) | `dotnet test --filter PwnedCheck` | ❌ Wave 0 |
| FEAT-004 | Rate limiter rejects 21st request in 5min window | integration | `dotnet test --filter PwnedCheckRate` | ❌ Wave 0 |
| X-cutting | v1.2.x `appsettings.Production.json` (no `Branding`) still boots | integration (WebApplicationFactory) | `dotnet test --filter Compat` | ❌ Wave 0 |

### Sampling Rate
- **Per task commit:** frontend task → `npm test -- <suite>`; backend task → `dotnet test --filter <Category>`.
- **Per wave merge:** both full suites.
- **Phase gate:** full suites green + manual UAT checklist below.

### Wave 0 Gaps
- [ ] All test files listed above — depend on Phase 2 scaffolding. If Phase 2 lags, create stub `.test.ts` / `.Tests.cs` files that skip with `Skip("awaiting Phase 2 harness")` so CI later auto-activates.
- [ ] Backend `PassReset.Tests/BrandingTests.cs`
- [ ] Backend `PassReset.Tests/PolicyTests.cs`
- [ ] Backend `PassReset.Tests/PwnedCheckTests.cs`
- [ ] Frontend `ClientApp/src/components/__tests__/BrandHeader.test.tsx`
- [ ] Frontend `ClientApp/src/components/__tests__/AdPasswordPolicyPanel.test.tsx`
- [ ] Frontend `ClientApp/src/components/__tests__/HibpIndicator.test.tsx`
- [ ] Frontend `ClientApp/src/components/__tests__/ClipboardCountdown.test.tsx`
- [ ] Frontend `ClientApp/src/utils/__tests__/sha1.test.ts`
- [ ] Frontend `ClientApp/src/utils/__tests__/clipboardClear.test.ts`
- [ ] Frontend `ClientApp/src/hooks/__tests__/useHibpCheck.test.tsx`

### Manual UAT (no automation possible)
- **FEAT-003** cross-browser clipboard permission prompt: Chrome / Edge / Firefox / Safari.
- **FEAT-001** fresh install without `C:\ProgramData\PassReset\brand\` existing → verify auto-create.
- **FEAT-004** real HIBP outage simulation (block `api.pwnedpasswords.com` at firewall) with both fail-open settings.
- **FEAT-002** deploy to non-domain-joined test env → verify panel stays hidden.

## Security Domain

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no (phase adds no auth surface) | — |
| V3 Session Management | no | — |
| V4 Access Control | yes (endpoints must remain unauthenticated like today but rate-limited) | `[EnableRateLimiting]` attribute |
| V5 Input Validation | yes | Prefix must be exactly 5 hex chars; `RequestSizeLimit(64)`; ModelState validation for shape |
| V6 Cryptography | yes | `crypto.subtle.digest('SHA-1', …)` — browser primitive, never hand-rolled |
| V7 Error Handling & Logging | yes | SIEM events on rate-limiter rejections + unavailable downgrade |
| V11 Business Logic | yes | `FailOpenOnPwnedCheckUnavailable` is a deliberate risk-accepted bypass; must be logged |
| V12 Files | yes | `PhysicalFileProvider` scoped to brand root — `ServeUnknownFileTypes=false` blocks arbitrary file serving |
| V14 Configuration | yes | Secrets remain `[JsonIgnore]`; branding values are safe-to-expose |

### Known Threat Patterns for ASP.NET Core / React stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Path traversal via `Branding.FaviconFileName=../../web.config` | Tampering | `PhysicalFileProvider` canonicalizes; additionally reject filenames containing `..`, `/`, `\` before forming URL |
| Plaintext password leaking to `/api/password/pwned-check` | Information Disclosure | Enforce body schema = exact 5 hex chars; reject any longer input with 400 |
| HIBP endpoint abuse for userbase enumeration (unlikely) | Information Disclosure | Rate limit 20/5min per IP — documented decision |
| XSS via `Branding.UsageText` | Tampering | Never `dangerouslySetInnerHTML`; render as plain Typography. If operator wants line breaks, split on `\n` only. |
| Clipboard content clobbering | Tampering | Read-before-clear guard (CONTEXT.md decision) |
| Favicon/logo request to external origin | CSP bypass | `img-src 'self' data:` — `/brand/*` is same-origin; reject `http(s)://` in filename config |
| Host header injection via helpdesk URL | XSS/Open redirect | `rel="noopener"` + validate URL starts with `http://` or `https://` at build time |

## Sources

### Primary (HIGH confidence)
- `src/PassReset.Web/Program.cs` — rate limiter pattern (line 137), CSP (line 183), static files (line 201), provider DI wiring (lines 98–128)
- `src/PassReset.Web/Controllers/PasswordController.cs` — controller pattern (lines 16, 57, 65), rate limit attribute (line 66)
- `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` — `GetDomainMaxPasswordAge` (line 209), `AcquireDomainEntry` (line 504)
- `src/PassReset.Common/IPasswordChangeProvider.cs` — interface surface (full file)
- `src/PassReset.Web/Models/ClientSettings.cs` — config shape, `[JsonIgnore]` pattern
- `src/PassReset.Web/ClientApp/src/api/client.ts` — fetch pattern (line 4, 13)
- `deploy/Install-PassReset.ps1` — create-if-missing idioms (lines 181, 438, 458)

### Secondary (MEDIUM confidence)
- [CITED: learn.microsoft.com/dotnet/api/microsoft.extensions.fileproviders.physicalfileprovider] — `PhysicalFileProvider` with `StaticFileOptions.RequestPath`
- [CITED: learn.microsoft.com/windows/win32/adschema/a-pwdproperties] — `pwdProperties` flag constants
- [CITED: developer.mozilla.org/en-US/docs/Web/API/SubtleCrypto/digest] — `crypto.subtle.digest('SHA-1', …)`
- [CITED: developer.mozilla.org/en-US/docs/Web/API/Clipboard/readText] — permission semantics across browsers

### Tertiary (LOW confidence — flag for verification during plan)
- A1: `PwnedPasswordChecker` internal API shape — observed only first 60 lines; planner must verify the method signature before choosing exposure strategy.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all libraries already in repo and verified.
- Architecture: HIGH — every pattern traced to existing code with file:line references.
- Pitfalls: HIGH — pitfalls grounded in observed patterns (CSP string, `maxPwdAge` negative-FILETIME math in existing method).
- Security: MEDIUM — ASVS mapping derived from stack knowledge, not pen-test evidence.

**Research date:** 2026-04-15
**Valid until:** 2026-05-15 (30 days — stable stack, no fast-moving deps introduced)

## RESEARCH COMPLETE
