---
phase: 01-v1-2-3-hotfix
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/PassReset.Web/Models/SmtpSettings.cs
  - src/PassReset.Web/Services/SmtpEmailService.cs
  - src/PassReset.Web/Services/CertificateTrust.cs
  - src/PassReset.Web/appsettings.Production.template.json
  - docs/appsettings-Production.md
  - CHANGELOG.md
autonomous: true
requirements:
  - BUG-001

must_haves:
  truths:
    - "Operator can configure SMTP to trust an internal-CA relay cert via thumbprint allowlist without silent bypass"
    - "When no thumbprint is configured, SMTP falls back to system trust store (default behavior unchanged)"
    - "Certificate-validation failures log thumbprint + subject + SslPolicyErrors; never log full cert"
    - "Existing appsettings.Production.json without TrustedCertificateThumbprints continues to work after upgrade"
  artifacts:
    - path: "src/PassReset.Web/Models/SmtpSettings.cs"
      provides: "TrustedCertificateThumbprints: string[] property"
      contains: "TrustedCertificateThumbprints"
    - path: "src/PassReset.Web/Services/CertificateTrust.cs"
      provides: "Pure function CertificateTrust.IsTrusted(cert, chain, errors, allowedThumbprints) for test-ready callback logic"
      exports: ["IsTrusted"]
    - path: "src/PassReset.Web/Services/SmtpEmailService.cs"
      provides: "SmtpClient wired with ServerCertificateValidationCallback using CertificateTrust.IsTrusted"
      contains: "ServerCertificateValidationCallback"
    - path: "docs/appsettings-Production.md"
      provides: "Documentation row for SmtpSettings.TrustedCertificateThumbprints"
      contains: "TrustedCertificateThumbprints"
  key_links:
    - from: "src/PassReset.Web/Services/SmtpEmailService.cs"
      to: "src/PassReset.Web/Services/CertificateTrust.cs"
      via: "ServerCertificateValidationCallback delegate"
      pattern: "CertificateTrust\\.IsTrusted"
    - from: "src/PassReset.Web/Services/SmtpEmailService.cs"
      to: "src/PassReset.Web/Models/SmtpSettings.cs"
      via: "IOptions<SmtpSettings>.TrustedCertificateThumbprints"
      pattern: "TrustedCertificateThumbprints"
---

<objective>
Fix BUG-001: Enable SMTP delivery against a relay that presents a certificate chained to an internal CA, without silent certificate-validation bypass.

Purpose: Unblock internal-CA SMTP deployments that currently fail the TLS handshake. Operators get an explicit, auditable opt-in mechanism (thumbprint allowlist) instead of a dangerous global "trust all" toggle.

Output: New `SmtpSettings.TrustedCertificateThumbprints` array, a testable pure `CertificateTrust.IsTrusted` helper, MailKit `SmtpClient.ServerCertificateValidationCallback` wired through it, documentation, and a CHANGELOG entry.
</objective>

<execution_context>
@C:/Users/Phibu/Claude-Projekte/AD-Passreset-Portal/.claude/get-shit-done/workflows/execute-plan.md
@C:/Users/Phibu/Claude-Projekte/AD-Passreset-Portal/.claude/get-shit-done/templates/summary.md
</execution_context>

<context>
@.planning/PROJECT.md
@.planning/ROADMAP.md
@.planning/REQUIREMENTS.md
@.planning/phases/01-v1-2-3-hotfix/RESEARCH.md

@src/PassReset.Web/Models/SmtpSettings.cs
@src/PassReset.Web/Services/SmtpEmailService.cs
@docs/appsettings-Production.md

<interfaces>
<!-- Key types expected from MailKit 4.15.1. Wire against these directly. -->

MailKit.Net.Smtp.SmtpClient:
  // public RemoteCertificateValidationCallback? ServerCertificateValidationCallback { get; set; }
  // Signature: bool (object sender, X509Certificate? cert, X509Chain? chain, SslPolicyErrors errors)

System.Security.Cryptography.X509Certificates:
  // X509Certificate2.Thumbprint => SHA-1 hex (uppercase, no spaces)

Existing SmtpSettings shape (extend, do not replace):
  Host, Port, UseStartTls / UseSsl, Username, Password, FromAddress, FromName, ...
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Add TrustedCertificateThumbprints setting + pure CertificateTrust helper</name>
  <files>src/PassReset.Web/Models/SmtpSettings.cs, src/PassReset.Web/Services/CertificateTrust.cs</files>
  <behavior>
    - CertificateTrust.IsTrusted returns true when SslPolicyErrors.None (happy path).
    - Returns true when errors != None but leaf cert thumbprint (normalized: uppercase, no spaces) is in allowedThumbprints (both SHA-1 40-char and SHA-256 64-char accepted).
    - Returns false when errors != None and no thumbprint match, OR allowedThumbprints is null/empty.
    - Thumbprint comparison is case-insensitive and ignores whitespace in the configured allowlist entries.
    - Cert of null returns false (defensive).
  </behavior>
  <action>
    Extend `SmtpSettings.cs` with:
    ```csharp
    /// <summary>
    /// Optional SHA-1 or SHA-256 thumbprints (hex, case-insensitive, spaces tolerated) of SMTP server certificates
    /// explicitly trusted even when the chain cannot be validated against the system store.
    /// Use this for internal-CA relays where installing the root into LocalMachine\Root is not feasible.
    /// Default (empty/null): rely solely on the OS trust store.
    /// </summary>
    public string[]? TrustedCertificateThumbprints { get; set; }
    ```

    Create new file `src/PassReset.Web/Services/CertificateTrust.cs`:
    ```csharp
    using System.Net.Security;
    using System.Security.Cryptography.X509Certificates;

    namespace PassReset.Web.Services;

    /// <summary>
    /// Pure (no I/O) certificate-trust decision helper. Extracted for testability — the SMTP
    /// callback in <see cref="SmtpEmailService"/> delegates here.
    /// </summary>
    /// <remarks>
    /// NEVER add an unconditional <c>return true</c>. Any bypass MUST go through an explicit
    /// thumbprint allowlist populated from configuration.
    /// </remarks>
    public static class CertificateTrust
    {
        public static bool IsTrusted(
            X509Certificate? cert,
            X509Chain? chain,
            SslPolicyErrors errors,
            IReadOnlyCollection<string>? allowedThumbprints)
        {
            if (errors == SslPolicyErrors.None) return true;
            if (cert is null) return false;
            if (allowedThumbprints is null || allowedThumbprints.Count == 0) return false;

            var c2 = cert as X509Certificate2 ?? new X509Certificate2(cert);
            var thumb = c2.Thumbprint; // SHA-1 upper hex, no spaces

            foreach (var allowed in allowedThumbprints)
            {
                if (string.IsNullOrWhiteSpace(allowed)) continue;
                var normalized = allowed.Replace(" ", "").Replace(":", "");
                if (string.Equals(normalized, thumb, StringComparison.OrdinalIgnoreCase)) return true;
                // Also support SHA-256: compute and compare if allowlist entry is 64 hex chars
                if (normalized.Length == 64)
                {
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    var sha256Hex = Convert.ToHexString(sha256.ComputeHash(c2.RawData));
                    if (string.Equals(normalized, sha256Hex, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }
    }
    ```

    Per RESEARCH.md: no `AcceptAllCertificates` flag. Keep `CheckCertificateRevocation` at MailKit default (true). Do not log raw cert data.
  </action>
  <verify>
    <automated>dotnet build src/PassReset.sln --configuration Release</automated>
  </verify>
  <done>
    SmtpSettings has nullable `TrustedCertificateThumbprints` property. CertificateTrust.cs compiles. `dotnet build -c Release` succeeds with zero warnings introduced by these files.
  </done>
</task>

<task type="auto">
  <name>Task 2: Wire callback into SmtpEmailService + log guardrails</name>
  <files>src/PassReset.Web/Services/SmtpEmailService.cs</files>
  <action>
    In the retry loop inside `SmtpEmailService.cs` immediately after `new SmtpClient()` (around line 53 per RESEARCH.md), set:
    ```csharp
    client.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
    {
        var trusted = CertificateTrust.IsTrusted(cert, chain, errors,
            _settings.TrustedCertificateThumbprints);
        if (!trusted)
        {
            _logger.LogError(
                "SMTP certificate validation failed: errors={Errors} subject={Subject} thumbprint={Thumbprint}",
                errors,
                cert?.Subject,
                (cert as System.Security.Cryptography.X509Certificates.X509Certificate2)?.Thumbprint);
        }
        else if (errors != System.Net.Security.SslPolicyErrors.None)
        {
            _logger.LogWarning(
                "SMTP certificate accepted via thumbprint allowlist despite chain errors: errors={Errors} subject={Subject} thumbprint={Thumbprint}",
                errors,
                cert?.Subject,
                (cert as System.Security.Cryptography.X509Certificates.X509Certificate2)?.Thumbprint);
        }
        return trusted;
    };
    ```

    Guardrails:
    - Do NOT set `client.CheckCertificateRevocation = false`.
    - Do NOT log raw `cert.RawData` or `cert.GetRawCertData()`.
    - Add an inline comment: `// NEVER return true unconditionally — bypass must flow through CertificateTrust.IsTrusted + TrustedCertificateThumbprints allowlist.`

    Add `using PassReset.Web.Services;` only if namespace resolution requires it (same namespace today — likely not needed).
  </action>
  <verify>
    <automated>dotnet build src/PassReset.sln --configuration Release</automated>
  </verify>
  <done>
    SmtpClient is configured with ServerCertificateValidationCallback that delegates to CertificateTrust.IsTrusted. Build clean. Grep for `return true;` inside SmtpEmailService.cs shows no unconditional returns.
  </done>
</task>

<task type="auto">
  <name>Task 3: Update appsettings template, docs, and CHANGELOG</name>
  <files>src/PassReset.Web/appsettings.Production.template.json, docs/appsettings-Production.md, CHANGELOG.md</files>
  <action>
    1. `appsettings.Production.template.json` — add an (empty) documented entry inside `SmtpSettings`:
       ```json
       "TrustedCertificateThumbprints": []
       ```
       With a preceding comment-equivalent key like `"_TrustedCertificateThumbprintsHelp": "Optional SHA-1/SHA-256 thumbprints (hex) of SMTP relay certs to trust when chain validation fails (e.g. internal CA not in system store). Empty = system trust only."` — OR just leave the empty array if the file doesn't already use helper keys. Follow existing template style.

    2. `docs/appsettings-Production.md` — add a new row in the SmtpSettings reference table:
       - Key: `SmtpSettings.TrustedCertificateThumbprints`
       - Type: `string[]` (optional)
       - Default: `[]` (empty → system trust store only)
       - Description: "Explicit SHA-1 (40 hex) or SHA-256 (64 hex) thumbprints of SMTP server certificates to trust when OS chain validation fails. Use only when installing the internal CA root into LocalMachine\\Root is not feasible. Spaces and colons are tolerated; comparison is case-insensitive. Validation failures are logged with thumbprint + subject (never the full cert). There is no global 'trust all' option by design."

    3. `CHANGELOG.md` — under `[Unreleased]` → `### Fixed`, add:
       ```
       - **SMTP**: Internal-CA-issued relay certificates can now be trusted via an opt-in
         `SmtpSettings.TrustedCertificateThumbprints` allowlist (SHA-1 or SHA-256). No silent
         bypass — entries must be explicitly configured. See `docs/appsettings-Production.md`.
         (BUG-001)
       ```
  </action>
  <verify>
    <automated>dotnet build src/PassReset.sln --configuration Release</automated>
  </verify>
  <done>
    Template has the new optional key. Docs row present. CHANGELOG `[Unreleased]` lists the fix. JSON template remains valid JSON (build step will surface obvious errors via config binding if anything breaks).
  </done>
</task>

</tasks>

<verification>
- `dotnet build src/PassReset.sln --configuration Release` succeeds with no new warnings.
- Grep `git grep -n "TrustedCertificateThumbprints"` returns hits in: `SmtpSettings.cs`, `CertificateTrust.cs` (or `SmtpEmailService.cs`), template, docs, CHANGELOG.
- Grep for `return true;` in `SmtpEmailService.cs` shows no unconditional bypass (only the delegated result of `CertificateTrust.IsTrusted`).
- Manual (staging, out of plan scope): point at a test SMTP with self-signed cert — connect fails. Add thumbprint → succeeds. One WARNING log line per connection. Wrong thumbprint → one ERROR log line.
</verification>

<acceptance_criteria>
From REQUIREMENTS.md BUG-001:
> When the SMTP relay presents a certificate chained to an internal CA, email delivery succeeds via opt-in trust configuration (explicit thumbprint allowlist or documented CA-trust option) without silently bypassing certificate validation. Documented in `docs/appsettings-Production.md`.

From ROADMAP.md Phase 1 success criterion #1:
> Operators can configure SMTP to trust an internal-CA-issued relay cert via documented, explicit trust config (thumbprint allowlist or CA-trust option) — no silent validation bypass.
</acceptance_criteria>

<pitfalls>
From RESEARCH.md BUG-001:
- **Silent bypass anti-pattern:** `return true;` in the callback. Add code comment "// NEVER return true unconditionally."
- **Thumbprint algo confusion:** SHA-1 thumbprints (40 hex) are what Cert MMC displays; also accept SHA-256 (64 hex) based on length.
- **CRL offline in air-gapped envs:** `X509ChainStatusFlags.RevocationStatusUnknown` fires; thumbprint allowlist dodges this cleanly.
- **Don't log full cert:** log thumbprint + subject + SslPolicyErrors only — never RawData.
- Do **not** set `SmtpClient.CheckCertificateRevocation = false` blindly.
</pitfalls>

<success_criteria>
- BUG-001 marked ready for release verification.
- No breaking change to existing appsettings.Production.json (new key is optional, defaults to system trust).
- Code is test-ready: `CertificateTrust.IsTrusted` is pure and callable from future xUnit tests (QA-001, Phase 2).
</success_criteria>

<commits>
Expected: one commit.
- `fix(web): support internal-CA SMTP trust via thumbprint allowlist (BUG-001)` — SmtpSettings + CertificateTrust + SmtpEmailService + template + docs + CHANGELOG.

Alternative two-commit split if preferred:
- `fix(web): add SmtpSettings.TrustedCertificateThumbprints and wire MailKit callback (BUG-001)`
- `docs(deps): document SmtpSettings.TrustedCertificateThumbprints`
</commits>

<output>
After completion, create `.planning/phases/01-v1-2-3-hotfix/01-SUMMARY.md` per template.

**After-phase step (not in this plan):** Tag `v1.2.3` happens only after plans 01, 02, 03 all pass verification; see CLAUDE.md release process.
</output>
