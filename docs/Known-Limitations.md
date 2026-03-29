# Known Limitations

This document lists known constraints and design trade-offs in PassReset. These are intentional decisions, not bugs.

## Platform

- **Windows-only**: The application uses `System.DirectoryServices.AccountManagement` and targets `net10.0-windows`. It will not build or run on Linux or macOS. CI runs on `windows-latest`.

## Deployment

- **Single-instance only**: Portal lockout state is held in an in-process `ConcurrentDictionary`. The IIS app pool `MaxProcesses` must remain at 1 (the default). If multiple worker processes or load-balanced instances are used, each maintains independent lockout counters, effectively multiplying the lockout threshold.

- **State lost on recycle**: IIS app pool recycles (default: every 29 hours) reset all lockout counters. An attacker who can predict the recycle schedule could time brute-force attempts to bypass lockout. AD's own lockout policy provides a backstop.

- **No zero-downtime deployment**: The installer stops the IIS site during file copy and restarts it after. There is a brief interruption during upgrades. For zero-downtime, a load balancer with two servers would be needed.

## Authentication

- **No multi-factor authentication**: The portal authenticates users by their current AD password only. There is no MFA challenge. Organizations requiring MFA for password changes should place the portal behind an MFA-aware reverse proxy or VPN.

- **No self-service account unlock**: The portal can only change passwords, not unlock locked accounts. Account unlock requires IT helpdesk intervention or a separate tool.

- **No passwordless authentication**: Users must know their current password to change it. If a user has forgotten their password entirely, they need helpdesk assistance.

## Password Policy

- **Fine-Grained Password Policies (FGPP/PSO)**: The portal reads `minPwdLength` and `minPwdAge` from the domain-level Default Domain Policy. If the organization uses Fine-Grained Password Policies (Password Settings Objects), the domain-level values may not match the effective policy for a specific user. AD itself enforces the correct policy during `ChangePassword()`, so the portal will still reject non-compliant passwords — but the pre-validation message may reference the wrong minimum length.

- **Password history enforcement via SetPassword fallback**: When `AllowSetPasswordFallback: true` is enabled (non-default), the administrative `SetPassword` API may bypass AD password history enforcement, allowing users to reuse previous passwords. This fallback is disabled by default.

## Networking

- **Rate limiting is per-IP**: Users behind a shared NAT or corporate proxy share the same rate limit bucket (5 requests / 5 minutes). In large offices with hundreds of users behind one public IP, legitimate users may occasionally hit the rate limit.

- **HIBP API dependency**: The HaveIBeenPwned breach check requires outbound HTTPS to `api.pwnedpasswords.com`. If the server cannot reach this endpoint (firewall, DNS failure), the behavior depends on the `FailOpenOnPwnedCheckUnavailable` setting (default: block the password change).

- **reCAPTCHA dependency**: When reCAPTCHA is enabled, the server must reach `www.google.com/recaptcha/api/siteverify`. The client browser must also load scripts from `google.com` and `gstatic.com`.

## Monitoring

- **No built-in APM**: The application has no Application Performance Monitoring integration (no Application Insights, Prometheus, or OpenTelemetry). Monitoring relies on IIS logs, the health endpoint, and SIEM syslog integration.

- **No persistent log storage by default**: Application logs go to the ASP.NET Core console logger. In IIS in-process hosting, these are captured by `stdout` logging (disabled by default in `web.config`). For persistent logs, enable stdout logging or configure a file logging provider.

## Frontend

- **No manual theme toggle**: Dark mode follows the operating system's `prefers-color-scheme` setting. Users cannot override this within the application.

- **No client-side routing**: The application is a single-page form with no URL-based navigation. The SPA fallback serves `index.html` for all non-API routes.

- **zxcvbn library is unmaintained**: The password strength meter uses `zxcvbn` v4.4.2 (last updated 2017). It has no known CVEs but is no longer receiving updates. A migration to `@zxcvbn-ts/core` is a future consideration.
