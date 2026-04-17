# Phase 9: Security Hardening - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-17
**Phase:** 09-security-hardening
**Areas discussed:** Generic error mapping (STAB-013), Audit events + test coverage (STAB-014/015), HTTPS/HSTS enforcement (STAB-016), Env-var secrets design (STAB-017)

---

## Generic error mapping (STAB-013)

| Option | Description | Selected |
|--------|-------------|----------|
| Only InvalidCredentials + UserNotFound | Minimal collapse per STAB-013 literal text | ✓ |
| Collapse all auth-related failures | Maximum opacity, hurts UX | |
| New AuthenticationFailed code | Clearer semantics, enum count bump | |

**User's choice:** Only InvalidCredentials + UserNotFound (Recommended)
**Notes:** Keeps ApiErrorCode pinned at 20 members; avoids TypeScript mirror update.

| Option | Description | Selected |
|--------|-------------|----------|
| IHostEnvironment.IsProduction | Env-based, no config knob | ✓ |
| WebSettings config flag | Operator can disable for debugging | |
| Both (env default + override) | Belt-and-suspenders | |

**User's choice:** IHostEnvironment.IsProduction (Recommended)
**Notes:** Matches existing UseDebugProvider pattern. Dev/test see real codes.

| Option | Description | Selected |
|--------|-------------|----------|
| "The credentials you supplied are not valid." | Reuse existing InvalidCredentials wording | ✓ |
| Neutral failure text | Fully opaque, vague | |
| You decide | Claude picks during planning | |

**User's choice:** Existing InvalidCredentials text (Recommended)
**Notes:** No new i18n string; minimal UI changes.

| Option | Description | Selected |
|--------|-------------|----------|
| SIEM stays granular | MapErrorCodeToSiemEvent unchanged | ✓ |
| Collapse SIEM too | Loses enumeration-attack detection | |

**User's choice:** SIEM stays granular (Recommended)
**Notes:** Wire response collapses; SIEM remains operator-visible.

---

## Audit events + test coverage (STAB-014/015)

| Option | Description | Selected |
|--------|-------------|----------|
| Extend SiemService with structured fields | Single source of truth | ✓ |
| Dedicated Serilog audit sink | Compliance-friendly but duplicates plumbing | |
| Both | Double writes, sync issues | |

**User's choice:** Extend SiemService (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| Allowlist DTO | No password field = compile-time safety | ✓ |
| Denylist scrubber | Flexible, new fields can leak | |
| Both | Belt-and-suspenders | |

**User's choice:** Allowlist DTO (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| WebApplicationFactory integration tests | End-to-end, covers middleware order | ✓ |
| Unit tests + fakes | Faster, misses middleware | |
| Both layers | More tests, more wiring confirmation | |

**User's choice:** WebApplicationFactory integration tests (Recommended)
**Notes:** Program.cs already partial-classed for this.

| Option | Description | Selected |
|--------|-------------|----------|
| Audit current 10 types, add AttemptStarted only if gap | Minimal enum churn | ✓ |
| Add AttemptStarted explicitly | Clearer attempt trail, 10→11 types | |
| You decide | Defer to planning | |

**User's choice:** Audit existing types first (Recommended)

---

## HTTPS/HSTS enforcement (STAB-016)

| Option | Description | Selected |
|--------|-------------|----------|
| App + installer binding check | Belt-and-suspenders | ✓ |
| Installer only | Breaks dev server | |
| App only | Current — operator can still misconfigure | |

**User's choice:** App + installer binding check (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| max-age=31536000; includeSubDomains (current) | One year + subdomains, no preload | ✓ |
| Add preload + 2-year max-age | Irreversible, org-policy territory | |
| Configurable in WebSettings | Adds surface and validator | |

**User's choice:** Current HSTS policy (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| Installer warns, doesn't block | Phase 7 Write-Warn pattern | ✓ |
| Installer refuses without HTTPS | Blocks offline-staging | |
| Docs only | Easy to miss | |

**User's choice:** Installer warn + doc (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| Keep WebSettings.EnableHttpsRedirect knob | Escape hatch for TLS-terminating proxy | ✓ |
| Force-enable in Production regardless | Removes operator escape hatch | |

**User's choice:** Keep existing knob (Recommended)

---

## Env-var secrets design (STAB-017)

| Option | Description | Selected |
|--------|-------------|----------|
| SMTP password + LDAP bind password + reCAPTCHA PrivateKey | Minimal stepping-stone scope | ✓ |
| All sensitive values | Wider net | |
| Also non-secret connection values | 12-factor, out of STAB-017 scope | |

**User's choice:** Minimal scope (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| ASP.NET Core double-underscore | Zero custom code | ✓ |
| Custom PASSRESET_ prefix | Cleaner names, adds code | |
| Both | Two code paths | |

**User's choice:** Double-underscore (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| dotnet user-secrets + docs | Standard ASP.NET Core tool | ✓ |
| appsettings.Development.json | Current, STAB-017 permits | |
| Both allowed with precedence | user-secrets wins | |

**User's choice:** user-secrets + docs (Recommended)

| Option | Description | Selected |
|--------|-------------|----------|
| Docs only — installer doesn't set env vars | Operator owns secrets | ✓ |
| Installer prompts + writes to AppPool | UX win, but installer holds plaintext | |
| Installer checks + warns | Read-only validation | |

**User's choice:** Docs only (Recommended)

---

## Claude's Discretion

- Exact RFC 5424 structured-data SD-ID + param names for audit event
- Whether validator rules are added when secrets are env-var-sourced
- Test class naming and file organization in PassReset.Tests
- Installer Write-Warn wording for binding check
- Whether STAB-013 IsProduction collapse lives in a helper or inlined at controller

## Deferred Ideas

- DPAPI / encrypted secrets at rest → v2.0 Phase 6 (V2-003)
- HSTS preload — operator decision
- AuthenticationFailed dedicated error code — rejected (enum count)
- Custom PASSRESET_ env-var prefix — rejected (redundant)
- Installer auto-setting AppPool env vars — rejected (plaintext in installer)
- Dedicated Serilog audit sink — rejected (single SIEM source)
- Removing EnableHttpsRedirect knob — rejected (proxy escape hatch)
