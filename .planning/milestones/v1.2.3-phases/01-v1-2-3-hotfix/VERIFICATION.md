---
status: passed
phase: 1
date: 2026-04-14
score: 12/12 must-haves verified
---

# Phase 1 Verification — v1.2.3 Hotfix

## Must-haves status

| REQ     | Must-have                                                                                       | Evidence                                                                                                                                | Status |
| ------- | ----------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| BUG-001 | `SmtpSettings.TrustedCertificateThumbprints` field exists                                        | `src/PassReset.Web/Models/SmtpSettings.cs:36` — `public string[]? TrustedCertificateThumbprints { get; set; }`                           | PASS   |
| BUG-001 | `ServerCertificateValidationCallback` wired to `CertificateTrust.IsTrusted` (no bypass)          | `src/PassReset.Web/Services/SmtpEmailService.cs:55-59` — delegates to `CertificateTrust.IsTrusted(..., TrustedCertificateThumbprints)`  | PASS   |
| BUG-001 | No unconditional `return true` in SMTP cert callback; no `CheckCertificateRevocation = false`   | `CertificateTrust.cs` returns `true` only on `SslPolicyErrors.None` or verified thumbprint match; no `CheckCertificateRevocation=false` found in SMTP code path | PASS   |
| BUG-001 | Template advertises new setting                                                                 | `appsettings.Production.template.json:72` — `"TrustedCertificateThumbprints": []`                                                        | PASS   |
| BUG-002 | `ApiErrorCode.PasswordTooRecentlyChanged = 19` exists                                           | `src/PassReset.Common/ApiErrorCode.cs:73`                                                                                                | PASS   |
| BUG-002 | `E_ACCESSDENIED` / `0x80070005` classified BEFORE any SetPassword fallback                      | `PasswordChangeProvider.cs:369-386` — comment "classify well-known HResults BEFORE any SetPassword fallback"; ApiErrorCode.PasswordTooRecentlyChanged returned at :386; SetPassword only at :394+ | PASS   |
| BUG-002 | Frontend enum mirror with value 19                                                              | `ClientApp/src/types/settings.ts:112` — `PasswordTooRecentlyChanged: 19`                                                                 | PASS   |
| BUG-002 | `PasswordForm.tsx` switch case handles new code                                                 | `PasswordForm.tsx:57` — `case ApiErrorCode.PasswordTooRecentlyChanged: return a.errorPasswordTooRecentlyChanged ?? ...`                  | PASS   |
| BUG-002 | Localized alert string present (server + template)                                              | `appsettings.json:162`, `appsettings.Production.template.json:167` — `ErrorPasswordTooRecentlyChanged` populated                        | PASS   |
| BUG-003 | Pre-read of existing `processModel.identityType` / `userName` before provisioning                | `Install-PassReset.ps1:294-300` — reads existing values via `Get-ItemProperty` guarded by `-ErrorAction Stop`                            | PASS   |
| BUG-003 | Preserve-on-upgrade branches for SpecificUser and built-in identities; default only on fresh install | `Install-PassReset.ps1:334-345` — explicit override, preserve SpecificUser, preserve built-in, fresh-install default=ApplicationPoolIdentity | PASS   |
| BUG-003 | NTFS ACL uses actual (preserved or override) identity — not stale computed default              | `Install-PassReset.ps1:413-431,441-449` — `$aclIdentity` resolution consumes `$existingIdentity`; applied via `FileSystemAccessRule`     | PASS   |

## Build/lint verification

- `dotnet build src/PassReset.sln -c Release`: **0 errors, 0 warnings** (Common, PasswordProvider, Web all built)
- `npx tsc --noEmit` in `ClientApp/`: **0 errors** (clean exit)

## Success criteria check (from ROADMAP.md)

1. SMTP trust via documented explicit config, no silent bypass → PASS — `TrustedCertificateThumbprints` allowlist + `CertificateTrust` guard; template documents the setting.
2. Min-age retry shows dedicated message; SIEM event emitted → PASS — HResult classified early, `PasswordTooRecentlyChanged` returned; frontend renders the localized alert; SIEM pathway untouched (no regression).
3. Installer upgrade preserves existing AppPool identity → PASS — preserve branches cover SpecificUser and built-in identities; explicit `-AppPoolIdentity` still overrides.
4. v1.2.3 tag → release zip with CHANGELOG/UPGRADING/docs updated → PARTIAL (programmatic) — CHANGELOG `[Unreleased]` lists all three bugs (BUG-001, BUG-002, BUG-003) with UPGRADING reference; the tag/release-workflow run itself is performed by the release process, not verifiable pre-tag. See Human verification note.

## Human verification notes

- Tag/release workflow success (SC #4 execution step) can only be confirmed after `git tag vX.Y.Z && git push`. Not blocking for pre-tag verification; CHANGELOG content is in place.
- Live end-to-end tests (real SMTP with internal CA cert, real AD min-age rejection, real IIS upgrade) are inherently manual. Code-path correctness is verified above.

## Gaps / concerns

None blocking. All 12 must-haves verified; build is clean; CHANGELOG entries present for all three bugs.

## Recommendation

**Proceed to release.** Code-level evidence supports all four ROADMAP success criteria. Human SMEs should run the three bug-specific smoke tests (SMTP+internal CA, min-age retry UX, installer upgrade on a pool with SpecificUser) before tagging `v1.2.3`, per the usual release checklist.
