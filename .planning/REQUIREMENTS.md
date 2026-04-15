# PassReset — Requirements

**Milestone chain:** v1.2.3 → v1.3.0 → v2.0.0
**Created:** 2026-04-14
**Source:** `Todo.MD` (formatted backlog)

> REQ IDs are stable references used by `ROADMAP.md` and phase plans.
> All requirements below are Active (to be delivered during this milestone chain).
> Validated (already-existing) capabilities live in `PROJECT.md`.

---

## v1.2.3 — Hotfix (3 P1 bugs)

### Bugs
- [ ] **BUG-001**: When the SMTP relay presents a certificate chained to an internal CA, email delivery succeeds via opt-in trust configuration (explicit thumbprint allowlist or documented CA-trust option) without silently bypassing certificate validation. Documented in `docs/appsettings-Production.md`.
- [ ] **BUG-002**: When AD rejects a password change because the domain's `minPwdAge` has not elapsed, the portal surfaces a dedicated `ApiErrorCode.PasswordTooRecentlyChanged` with a localized user-facing message (no generic "Unexpected Error"). SIEM event logged appropriately.
- [ ] **BUG-003**: `deploy/Install-PassReset.ps1` preserves the existing IIS AppPool identity during upgrade (does not reset to `ApplicationPoolIdentity`) unless explicitly overridden via parameter. Documented in `UPGRADING.md`.

---

## v1.3.0 — UX + Quality

### UX Features
- [ ] **FEAT-001**: Operators can customize portal branding (company name, portal name, helpdesk website URL, helpdesk email, usage/limitations text block, logo image, favicon) via `appsettings.Production.json` → `ClientSettings`. Images served from an upgrade-safe path. Defaults preserve current look. No tech-stack change.
- [ ] **FEAT-002**: When `ClientSettings.ShowAdPasswordPolicy` is enabled (default: `false`), the portal displays the effective AD password policy minimum requirements (min length, complexity, history) above the new-password field. If the AD query fails, the panel is hidden (fails closed).
- [ ] **FEAT-003**: When `ClientSettings.ClipboardClearSeconds` is > 0 (default: `30`) and the user used the password generator, the clipboard is cleared after the configured delay via `navigator.clipboard.writeText('')` — only if the clipboard content still matches the generated password (no clobbering of unrelated user content).
- [x] **FEAT-004**: On new-password field blur, the UI displays a HaveIBeenPwned breach status indicator (safe / breached + count) using the existing `PwnedPasswordChecker` k-anonymity API. Debounced. Honors `FailOpenOnPwnedCheckUnavailable`. Plaintext password never leaves the client.

### Quality
- [x] **QA-001**: Automated test foundation in place — xUnit (backend) for provider logic, error mapping, SIEM, lockout decorator; Vitest + React Testing Library (frontend) for components, hooks, utilities. `dotnet test` and `npm test` run in CI. Coverage thresholds defined. Release workflow blocks on test failures.

---

## v2.0.0 — Platform evolution

### Research + PoC
- [ ] **V2-001**: Research + PoC for multi-OS support — a documented path to Linux/Docker without `System.DirectoryServices.AccountManagement`; PoC Docker image performs a password change against a test AD; decision captured (Novell.Directory.Ldap.NETStandard vs alternative). Stays a research phase; full migration deferred if blockers found.
- [ ] **V2-002**: Local password-protection database — operator-managed banned words/terms list + attempted-pwned lookup table; provider consults the local store and enforces bans even when stricter than AD policy; LICENSE-compatible integration if borrowing from lithnet/ad-password-protection.
- [ ] **V2-003**: Secure config storage — secrets in `appsettings.Production.json` (SMTP, reCAPTCHA, LDAP creds) no longer stored as cleartext on disk by default. Supported mechanism chosen from DPAPI / ASP.NET Core Data Protection / Credential Manager / optional Key Vault adapter. Clear upgrade path for existing installs.

---

## Cross-cutting constraints (apply to every requirement)

- **No breaking config changes** across minor versions — existing `appsettings.Production.json` continues to work after upgrade to v1.2.3 and v1.3.0.
- **Commit convention** enforced by `.githooks/commit-msg` — types: `feat fix refactor docs chore test ci perf style`; scopes: `web provider common deploy docs ci deps security installer`.
- **CI**: GitHub Actions on `windows-latest`; release triggered by `git tag vX.Y.Z` → `release.yml`.
- **Tech stack locked**: ASP.NET Core 10 / React 19 / MUI 6 / Vite — no stack changes during v1.2.3 or v1.3.0. v2.0 may introduce cross-platform infrastructure.
- **Documentation**: `README.md`, `CHANGELOG.md`, and affected `docs/*.md` updated as part of each release.

---

## Out of Scope

- **MSI packaging** — explicitly deferred after the 2026-04-13 rollback. PowerShell installer is the supported deployment path.
- **Password reset via email/SMS** — portal is *change* only.
- **SSO / federation adapters** — direct-AD portal.
- **Stack modernization** (e.g., migrating to .NET 11, React 20) — not part of this milestone chain.

---

## Traceability

> Each REQ-ID maps to exactly one phase. Updated 2026-04-14 by roadmap creation.

| REQ-ID | Phase | Plan | Status |
|---|---|---|---|
| BUG-001 | Phase 1 (v1.2.3 Hotfix) | TBD | Active |
| BUG-002 | Phase 1 (v1.2.3 Hotfix) | TBD | Active |
| BUG-003 | Phase 1 (v1.2.3 Hotfix) | TBD | Active |
| FEAT-001 | Phase 3 (v1.3 UX Features) | TBD | Active |
| FEAT-002 | Phase 3 (v1.3 UX Features) | TBD | Active |
| FEAT-003 | Phase 3 (v1.3 UX Features) | TBD | Active |
| FEAT-004 | Phase 3 (v1.3 UX Features) | TBD | Active |
| QA-001 | Phase 2 (v1.3 Test Foundation) | TBD | Active |
| V2-001 | Phase 4 (v2.0 Multi-OS PoC) | TBD | Active |
| V2-002 | Phase 5 (v2.0 Local Password DB) | TBD | Active |
| V2-003 | Phase 6 (v2.0 Secure Config Storage) | TBD | Active |

**Coverage:** 11/11 requirements mapped ✓ · 0 orphans
