# ReKey

**Self-service Active Directory password change portal.**
Built on .NET 10 LTS · React 19 · MUI 6 · Vite · IIS on Windows Server 2022.

> Inspired by [PassCore](https://github.com/unosquare/passcore) by Unosquare LLC — fully rewritten on modern foundations with email notifications, password expiry reminders, HaveIBeenPwned integration, reCAPTCHA v3, and a clean contemporary UI.

![ReKey UI](docs/screenshot.png)

---

## Features

| Feature | Details |
|---|---|
| AD password change | `System.DirectoryServices.AccountManagement` — domain-joined or explicit LDAP |
| Password strength meter | zxcvbn score with live visual feedback |
| Password generator | Crypto-secure, configurable entropy |
| Pwned password check | HaveIBeenPwned k-anonymity API |
| reCAPTCHA v3 | Server-side score validation (≥ 0.5) |
| Password-changed email | MailKit, STARTTLS/SMTPS, Mimecast-compatible |
| Expiry reminder emails | Daily background service, configurable threshold |
| AD group allow/block lists | Restrict which users can self-serve |
| Minimum password age | Enforces AD `minPwdAge` policy |
| Must-change-at-next-logon | Clears `pwdLastSet` flag after successful change |
| Rate limiting | Built-in ASP.NET Core fixed-window limiter |
| Security headers | CSP, HSTS, X-Frame-Options, Referrer-Policy, Permissions-Policy |
| Debug provider | Full UI testing without an AD connection |

---

## Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 10 LTS (`net10.0-windows`) |
| Web framework | ASP.NET Core minimal hosting |
| AD integration | `System.DirectoryServices` / `AccountManagement` |
| Email | MailKit 4.x |
| Frontend | React 19 + TypeScript |
| UI components | MUI v6 (Material UI) |
| Build tool | Vite 6 |
| Password scoring | zxcvbn |
| Deployment | IIS 10, Windows Server 2022 |

---

## Project Structure

```
rekey/
├── src/
│   ├── ReKey.Common/             # Shared interfaces and error types
│   ├── ReKey.PasswordProvider/   # AD password provider (Windows-only)
│   └── ReKey.Web/                # ASP.NET Core app + React frontend
│       ├── ClientApp/            # React 19 + Vite source
│       ├── Controllers/          # API endpoints
│       ├── Models/               # Config and request models
│       ├── Services/             # Email + background services
│       ├── Helpers/              # Debug/no-op providers
│       ├── appsettings.json      # Default configuration
│       └── Program.cs            # App entry point + DI wiring
├── deploy/
│   ├── Publish-ReKey.ps1         # Build frontend + dotnet publish
│   ├── Install-ReKey.ps1         # IIS site/pool/cert/permissions setup
│   └── AD-ServiceAccount-Setup.md
└── docs/
    ├── IIS-Setup.md              # IIS prerequisites and certificate guide
    └── screenshot.png            # UI preview
```

---

## Quick Start — Development

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 20+](https://nodejs.org/)

### Run the backend

```bash
cd src/ReKey.Web
dotnet run
```

### Run the frontend (hot-reload)

```bash
cd src/ReKey.Web/ClientApp
npm install
npm run dev
```

The Vite dev server proxies `/api` to `https://localhost:5001`.

### Debug mode (no AD required)

In `src/ReKey.Web/appsettings.Development.json`:

```json
{
  "WebSettings": {
    "UseDebugProvider": true
  }
}
```

Use these usernames to exercise specific error states:

| Username | Result |
|---|---|
| *(any other)* | Success |
| `error` | Generic error |
| `invalidCredentials` | Wrong current password |
| `userNotFound` | User not found |
| `changeNotPermitted` | Not allowed |
| `pwnedPassword` | Pwned password |
| `passwordTooYoung` | Too recently changed |

---

## Configuration

Settings live in `appsettings.json` (defaults) and `appsettings.Production.json` (overrides, never committed).

### `PasswordChangeOptions`
```json
{
  "PasswordChangeOptions": {
    "UseAutomaticContext": true,
    "IdTypeForUser": "UserPrincipalName",
    "DefaultDomain": "yourdomain.com",
    "ClearMustChangePasswordFlag": true,
    "EnforceMinimumPasswordAge": true,
    "RestrictedAdGroups": ["Domain Admins", "Enterprise Admins"],
    "AllowedAdGroups": [],
    "LdapHostnames": ["dc01.yourdomain.com"],
    "LdapPort": 389,
    "LdapUsername": "",
    "LdapPassword": ""
  }
}
```

- `UseAutomaticContext: true` — domain-joined server, no credentials needed
- `AllowedAdGroups: []` (empty) — all users permitted; add groups to restrict access
- Block list takes priority over allow list

### `SmtpSettings`
```json
{
  "SmtpSettings": {
    "Host": "smtp-relay.yourdomain.com",
    "Port": 587,
    "UseSsl": true,
    "FromAddress": "rekey@yourdomain.com",
    "FromName": "ReKey Self-Service"
  }
}
```

Port `587` = STARTTLS · Port `465` = SMTPS · Leave `Host` empty to disable email.

---

## Deployment

See [`docs/IIS-Setup.md`](docs/IIS-Setup.md) for the full step-by-step guide including certificate setup.

### Quick deploy

```powershell
# 1. Build
.\deploy\Publish-ReKey.ps1

# 2. Install to IIS (run as Administrator)
.\deploy\Install-ReKey.ps1 `
    -AppPoolIdentity "CORP\svc-rekey" `
    -AppPoolPassword "S3cr3t!" `
    -CertThumbprint "A1B2C3D4..."

# 3. Edit C:\inetpub\ReKey\appsettings.Production.json
```

For AD service account delegation, see [`deploy/AD-ServiceAccount-Setup.md`](deploy/AD-ServiceAccount-Setup.md).

---

## API

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/password` | Returns `ClientSettings` for UI initialisation |
| `POST` | `/api/password` | Submit password change |
| `GET` | `/health` | Health check |

---

## License

MIT — © 2024–2025 Philippe Buschmann.

Inspired by [PassCore](https://github.com/unosquare/passcore) © 2016–2022 Unosquare LLC (MIT).
See [LICENSE](LICENSE) for full text and attribution.
