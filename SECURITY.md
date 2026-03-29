# Security Policy

## Supported Versions

| Version | Supported |
|---------|-----------|
| 1.0.x   | Yes       |
| < 1.0   | No        |

## Reporting a Vulnerability

If you discover a security vulnerability in PassReset, please report it responsibly.

**Do not open a public GitHub issue for security vulnerabilities.**

Instead, please use one of the following channels:

1. **GitHub Private Vulnerability Reporting**: Use the [Security Advisories](../../security/advisories/new) feature on this repository to report privately.
2. **Email**: Contact the maintainer directly via the email listed in the Git commit history.

### What to Include

- A clear description of the vulnerability
- Steps to reproduce the issue
- The potential impact
- Any suggested remediation (optional)

### Response Timeline

- **Acknowledgment**: Within 48 hours of receipt
- **Initial assessment**: Within 7 days
- **Fix timeline**: Depends on severity; critical issues will be patched within 14 days

### Scope

The following are in scope for security reports:

- Authentication bypass or credential exposure
- LDAP injection or other injection attacks
- Cross-site scripting (XSS) or cross-site request forgery (CSRF)
- Rate limiting or lockout bypass
- Information disclosure (e.g., password leakage in logs, error messages, or API responses)
- Denial of service affecting the password change portal
- Configuration issues that could lead to privilege escalation

### Out of Scope

- Active Directory security issues not related to this application
- Social engineering or phishing attacks
- Vulnerabilities in third-party dependencies with no proof of exploitability in this context
- Issues requiring physical access to the server

## Security Architecture

PassReset implements defense-in-depth for password security:

- **Transport**: HTTPS enforced with HSTS (1-year max-age, includeSubDomains)
- **Headers**: CSP, X-Frame-Options DENY, X-Content-Type-Options nosniff, Referrer-Policy, Permissions-Policy
- **Rate limiting**: Per-IP fixed window (5 requests / 5 minutes)
- **Portal lockout**: Per-username failure tracking (3 attempts / 30 minutes) independent of AD lockout
- **Breach checking**: HaveIBeenPwned k-anonymity API (only SHA-1 prefix sent, never the full hash)
- **Bot protection**: reCAPTCHA v3 with score and action verification
- **Privileged account blocking**: Domain Admins, Enterprise Admins, Schema Admins, Administrators blocked by default
- **Credentials**: Never logged, never returned in API responses, never stored beyond the LDAP bind operation
- **SIEM integration**: All security events forwarded via RFC 5424 syslog with configurable email alerts
