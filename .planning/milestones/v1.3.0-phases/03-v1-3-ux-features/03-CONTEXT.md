# Phase 3: v1.3 UX Features - Context

**Gathered:** 2026-04-15
**Status:** Ready for planning
**Source:** /gsd:discuss-phase (interactive)

<domain>
## Phase Boundary

Deliver four operator/user-facing UX features on top of the v1.2.3 codebase, shipped together as v1.3.0:

- **FEAT-001** Operator branding (company name, portal name, helpdesk URL/email, usage text, logo, favicon) via `appsettings.Production.json` → `ClientSettings.Branding`, served from an upgrade-safe path.
- **FEAT-002** Optional AD password-policy panel above the new-password field, gated on `ClientSettings.ShowAdPasswordPolicy` (default false), hidden on AD query failure.
- **FEAT-003** Clipboard clearing after `ClipboardClearSeconds` when the generator was used, only if clipboard still holds the generated password.
- **FEAT-004** HIBP breach indicator on new-password blur (safe / breached + count), debounced, honoring `FailOpenOnPwnedCheckUnavailable`, plaintext never leaves the client.

**Visual, layout, typography, copywriting, and accessibility contracts are already locked in [03-UI-SPEC.md](./03-UI-SPEC.md).** This context captures the backend/behavior decisions that UI-SPEC did not cover.

No breaking changes to pre-v1.3 `appsettings.Production.json`. Tech stack unchanged (ASP.NET Core 10 / React 19 / MUI 6 / Vite).

</domain>

<decisions>
## Implementation Decisions

### FEAT-001 — Branding

- **Asset storage path:** `C:\ProgramData\PassReset\brand\` (outside app install dir). `Install-PassReset.ps1` must never touch this folder on upgrade. Default path is used when `ClientSettings.Branding.AssetRoot` is null.
- **Static serving:** ASP.NET Core serves `/brand/*` from the asset root via `PhysicalFileProvider` + `StaticFileOptions` mapped in `Program.cs`. Path is configurable via `ClientSettings.Branding.AssetRoot`.
- **Config shape:** Nested object on `ClientSettings`:
  ```
  ClientSettings.Branding:
    CompanyName, PortalName, HelpdeskUrl, HelpdeskEmail, UsageText,
    LogoFileName, FaviconFileName, AssetRoot
  ```
  When `Branding` is null or omitted, the portal renders the existing default look (backward compat).
- **Helpdesk rendering:** URL and email both render as links — `<a href={url} target="_blank" rel="noopener">` and `<a href="mailto:{email}">`. Block only appears if at least one field is configured.
- **Favicon:** Served via a `<link rel="icon">` tag whose href is injected at runtime from `ClientSettings.Branding.FaviconFileName` (fallback: existing default favicon).

### FEAT-002 — AD Password Policy Panel

- **Data source:** Default domain policy via RootDSE (query `minPwdLength`, `pwdProperties`, `pwdHistoryLength`, `minPwdAge`, `maxPwdAge` on the domain NC head). Fine-grained PSOs are out of scope for v1.3 (deferred).
- **Caching:** In-memory TTL cache, 1 hour, process-wide (`IMemoryCache` or equivalent). Cache key is the domain DN. AD query failure is cached as "unavailable" for a shorter TTL (e.g., 60s) to avoid hammering AD while a DC is down.
- **Failure mode:** On AD query failure the panel renders nothing (fails closed, as per FEAT-002 requirement).
- **Endpoint:** `GET /api/password/policy` returns `{ minLength, requiresComplexity, historyLength, minAgeDays, maxAgeDays }` or `404`/empty payload when unavailable. Called once on portal mount when `ShowAdPasswordPolicy=true`.
- **Provider contract:** Add `GetEffectivePasswordPolicyAsync()` to `IPasswordChangeProvider`; implemented on `PasswordChangeProvider` and stubbed on `DebugPasswordChangeProvider`.

### FEAT-003 — Clipboard Clearing

- **Trigger:** Password generator `onCopy` handler starts a timer for `ClipboardClearSeconds` (default 30, 0 = disabled).
- **Safety check:** On timer elapse, call `navigator.clipboard.readText()`; if the current clipboard value equals the generated password, call `navigator.clipboard.writeText('')`. Otherwise do nothing.
- **Permission prompt:** Accepted — Firefox and Safari may prompt for clipboard-read permission. This is the only way to guarantee we don't clobber unrelated content the user may have copied after generation.
- **Regeneration:** If the user regenerates before the timer fires, the previous timer is canceled and the tracked "last generated" value updates to the new one.
- **No-op paths:** If `ClipboardClearSeconds=0`, never start the timer. If `navigator.clipboard` or `readText`/`writeText` is unavailable (old browsers), silently no-op.

### FEAT-004 — HIBP Blur Indicator

- **Wiring:** New endpoint `POST /api/password/pwned-check` — browser computes SHA-1 of the new password client-side (WebCrypto), sends the first 5 hex chars as JSON body. Server calls HIBP via the existing `PwnedPasswordChecker` (k-anonymity range API), returns `{ breached: bool, count: number }` to the client.
- **Plaintext boundary:** The plaintext password never leaves the client. Only the 5-char SHA-1 prefix crosses the wire to our server, then to HIBP.
- **Debounce:** Blur handler debounces 400ms before firing the request; in-flight requests are aborted if the field changes again.
- **Fail-open:** Honors `FailOpenOnPwnedCheckUnavailable`. On HIBP failure and `FailOpenOnPwnedCheckUnavailable=true`, endpoint returns `{ breached: false, unavailable: true }` and the indicator renders a neutral "check unavailable" state. When `false`, endpoint returns 503 and the indicator renders an error state.
- **Rate limiting:** Endpoint reuses the per-IP fixed-window limiter pattern but with a more permissive budget (e.g., 20 req / 5 min) since it's called on blur, not on submit. Limiter rejections log a SIEM event.
- **SIEM:** Blur-check failures are NOT logged per-request (too noisy); only aggregate limiter rejections and unavailable-downgrade events.

### Cross-cutting

- **Backward compat:** Every new `ClientSettings` key has a safe default; omitting the keys produces the current v1.2.3 behavior. Verified by an upgrade-config fixture test in Phase 2's Vitest/xUnit suite if that phase lands first.
- **Dark mode:** All four components respect the existing `prefers-color-scheme` theming (per UI-SPEC).
- **CSP:** `connect-src` unchanged from v1.2.3 (HIBP is proxied server-side). `img-src` must allow `'self'` so `/brand/*` images render (already the case).

### Claude's Discretion

- Concrete MUI component choices inside each new component (as long as UI-SPEC contracts are met).
- Exact DI wiring of the AD policy cache (`IMemoryCache` vs a tiny custom TTL dict).
- Exact SHA-1 WebCrypto helper location (`utils/sha1.ts` is the default expectation).
- Internal file organization inside `ClientApp/src/components/` (may introduce a `branding/` sub-folder).
- Test layout inside new Phase 2 test projects (Phase 2 plan owns the convention).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Phase artifacts
- `.planning/phases/03-v1-3-ux-features/03-UI-SPEC.md` — locked visual/interaction/a11y/copy contracts for all four features (BrandHeader, AdPasswordPolicyPanel, HibpIndicator, ClipboardCountdown).
- `.planning/ROADMAP.md` — Phase 3 goal, success criteria, requirements IDs (FEAT-001..FEAT-004).
- `.planning/REQUIREMENTS.md` — Full text of FEAT-001 through FEAT-004 and cross-cutting constraints.

### Codebase anchors (existing patterns to extend)
- `src/PassReset.Web/Models/ClientSettings.cs` — target for `Branding` nested object.
- `src/PassReset.Web/Program.cs` — where `PhysicalFileProvider` static-file mapping and new endpoints register.
- `src/PassReset.Web/Controllers/PasswordController.cs` — pattern for new `GET /api/password/policy` and `POST /api/password/pwned-check` endpoints.
- `src/PassReset.Common/IPasswordChangeProvider.cs` — interface to extend with `GetEffectivePasswordPolicyAsync`.
- `src/PassReset.PasswordProvider/PasswordChangeProvider.cs` — AD call site; `GetDomainMaxPasswordAge` shows the RootDSE pattern.
- `src/PassReset.PasswordProvider/PwnedPasswordChecker.cs` — reused by the new pwned-check endpoint.
- `src/PassReset.Web/ClientApp/src/App.tsx` — hard-coded header replaced by `BrandHeader`.
- `src/PassReset.Web/ClientApp/src/components/PasswordForm.tsx` — integration point for `AdPasswordPolicyPanel`, `HibpIndicator`, `ClipboardCountdown`.
- `src/PassReset.Web/ClientApp/src/utils/passwordGenerator.ts` — `onCopy` + clipboard-clear timer integration.
- `src/PassReset.Web/ClientApp/src/hooks/useSettings.ts` — extend to surface `Branding` block.

### Deployment
- `deploy/Install-PassReset.ps1` — must create `C:\ProgramData\PassReset\brand\` if missing and must NOT remove/overwrite it on upgrade.

</canonical_refs>

<specifics>
## Specific Ideas

- Default branding fallback: `PortalName="PassReset"`, `CompanyName=""`, no logo, existing teal MUI theme (`#0b6366`).
- HIBP client-side hashing: use `crypto.subtle.digest('SHA-1', ...)` — already available in every browser the portal targets (no polyfill).
- Clipboard-clear timer UI: UI-SPEC defines `ClipboardCountdown` visual; backend just needs to expose `ClipboardClearSeconds` via ClientSettings (no new endpoint).
- AD policy endpoint is intentionally separate from `GET /api/password` (settings) because the policy TTL is shorter and the query is optional.

</specifics>

<deferred>
## Deferred Ideas

- **Fine-grained Password Settings Objects (PSO) resolution** — v2.0 candidate. Requires `msDS-ResultantPSO` resolution after the user types a username, which breaks the UI-SPEC's "panel above new-password field on mount" placement. Capture in backlog.
- **Operator branding upload UI** — no web UI for uploading logos/favicons; operator drops files into `C:\ProgramData\PassReset\brand\` manually. Admin upload UI is a future phase.
- **CSP-strict HIBP direct-from-browser path** — considered and rejected for v1.3 to keep server-side `FailOpenOnPwnedCheckUnavailable` authority and existing SIEM observability. Revisit only if server HIBP hop becomes a performance issue.
- **Clipboard-clear without read permission** — considered; rejected because the "only clear if clipboard still matches" guarantee requires a readback.
- **Multi-language branding / i18n** — not in scope for v1.3. UsageText is a single string block.

</deferred>

---

*Phase: 03-v1-3-ux-features*
*Context gathered: 2026-04-15 via /gsd:discuss-phase*
