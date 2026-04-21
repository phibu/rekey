# appsettings.Production.json Reference

This file overrides `appsettings.json` in production. Place it in the same folder as `PassReset.Web.exe` (e.g. `C:\inetpub\PassReset\`). The install script creates a starter copy automatically — edit it before starting the site.

---

## Authoritative schema

The authoritative source of truth for every valid configuration key is
[`src/PassReset.Web/appsettings.schema.json`](../src/PassReset.Web/appsettings.schema.json).
It ships in every release zip (copied into the deploy root alongside the template) so the
installer and runtime can validate your live config against the exact schema of the installed
version.

| Aspect | Detail |
|---|---|
| Format | [JSON Schema Draft 2020-12](https://json-schema.org/draft/2020-12/schema) |
| Validation scope | Required keys, types (`boolean` / `integer` / `string` / `array` / `object`), enums, `pattern` regexes, numeric `minimum` / `maximum`, per-leaf `default` values |
| Out-of-scope in schema | Cross-field invariants (e.g. "`SmtpSettings.Host` set ⇒ `Port` must be 1..65535") — those are enforced at runtime by `IValidateOptions<T>` implementations (per decision D-04) |
| Custom markers | `x-passreset-obsolete: true` flags a deprecated key. `x-passreset-obsolete-since: "1.3.2"` records the version it was deprecated in. The installer's ConfigSync uses these markers to prompt for removal on upgrade. |
| Pre-flight | `Test-Json -Path appsettings.Production.json -SchemaFile appsettings.schema.json` runs inside `Install-PassReset.ps1` on every upgrade, BEFORE any sync. Failure halts install with an actionable field-path error. |
| Host requirement | PowerShell 7+ on the install host (`Test-Json -SchemaFile` is unavailable in Windows PowerShell 5.1). |

Operators who want to pre-validate an edit before restarting the app can run the same pre-flight
command locally:

```powershell
pwsh -Command "Test-Json -Path 'C:\inetpub\PassReset\appsettings.Production.json' -SchemaFile 'C:\inetpub\PassReset\appsettings.schema.json' -ErrorVariable errors"
```

---

## Startup validation

Phase 8 introduced fail-fast runtime validation. Every options class is registered via
`AddOptions<T>().Bind(section).ValidateOnStart()` with an `IValidateOptions<T>` implementation —
so misconfigured values are detected at DI-build time, not on the first request.

**Failure flow:**

1. `builder.Build()` (or equivalently the first `app.Run()`) throws
   `OptionsValidationException` with one or more failure messages.
2. `StartupValidationFailureLogger` intercepts the exception and writes each failure message
   to the Windows **Application** Event Log under source `PassReset`, **event ID 1001**, type
   Error.
3. The exception re-throws. IIS returns HTTP **502** to the next request; the ASP.NET Core
   Module records the entry-point crash in its own stdout log.

**Error message format (D-08):**

```
{Section}.{Field}: {reason} (got "{actual}"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.
```

Example:

```
PasswordChangeOptions.LdapPort: must be integer between 1 and 65535 (got "0"). Edit appsettings.Production.json or run Install-PassReset.ps1 -Reconfigure.
```

Sensitive fields (`SmtpSettings.Password`, `ClientSettings.Recaptcha.PrivateKey`, LDAP
credentials, etc.) are rendered as `(got "<redacted>")` — validator unit tests assert that the
offending secret never leaks into the message.

### Diagnosing a 502 after upgrade or config edit

1. Open **Event Viewer** → **Windows Logs** → **Application**.
2. Filter by **Source = `PassReset`**, **Event ID = 1001**.
3. Read the failure lines — each one names the exact JSON path and the offending value.
4. Edit `C:\inetpub\PassReset\appsettings.Production.json` (or re-run the installer with
   `-Reconfigure`).
5. Reload the pool:

   ```powershell
   Restart-WebAppPool -Name PassResetPool
   ```

**If no `PassReset` event appears in Event Viewer:** the Event Log source is not registered on
this host. Re-run `Install-PassReset.ps1` — the installer registers the source idempotently
during the prerequisites phase (requires admin, which the installer already asserts via
`#Requires -RunAsAdministrator`).

---

## Validation rules per options class

The following runtime validation invariants are enforced in addition to the structural rules
in `appsettings.schema.json`. Failures surface in the same Event Log path (source `PassReset`,
event ID 1001) as D-08 messages.

| Options class | Validation invariants |
|---|---|
| `PasswordChangeOptions` | `UseAutomaticContext=false` ⇒ `LdapHostnames` non-empty; `LdapPort` in `1..65535`. D-08 suffix with installer remediation hint on every failure. |
| `WebSettings` | Type-only schema checks. The `UseDebugProvider=true` ⇒ `Development` environment guard stays inline in `Program.cs` (cannot access `IHostEnvironment` from within `IValidateOptions<T>`). |
| `SmtpSettings` | When `Host` is non-empty: `Port` in `1..65535`; `FromAddress` contains `@`; `Username` and `Password` are both set or both empty (partial credentials rejected). |
| `SiemSettings` | When `Syslog.Enabled=true`: `Host` non-empty, `Port` in `1..65535`, `Protocol` ∈ {`Udp`, `Tcp`}. When `AlertEmail.Enabled=true`: at least one recipient, each containing `@`. `AlertOnEvents` entries must map to valid `SiemEventType` enum members. |
| `EmailNotificationSettings` | When `Enabled=true`: `SmtpSettings.Host` must be set upstream (cross-section check in `Program.cs`). |
| `PasswordExpiryNotificationSettings` | When `Enabled=true`: `DaysBeforeExpiry >= 1`; `NotificationTimeUtc` matches `^\d{2}:\d{2}$`; `PassResetUrl` starts with `https://`. |
| `ClientSettings` | When `Recaptcha.Enabled=true`: `SiteKey` and `PrivateKey` both non-empty; `MinimumScore` in `[0.0, 1.0]`. |

> **Secret redaction:** Validation failures NEVER echo secret values. Password, PrivateKey,
> LDAP bind password, and SMTP password fields always surface as `(got "<redacted>")` in the
> Event Log and in any exception message.

---

## Serilog (file logging)

The default configuration writes to `%SystemDrive%\inetpub\logs\PassReset\passreset-YYYYMMDD.log` (IIS convention, outside wwwroot so files are never web-accessible). The installer creates this folder and grants `Modify` permission to the app pool identity.

**What is logged:**
- **Errors / warnings** — full structured detail: exception message, stack trace, event properties (username, IP, group, error code, correlation).
- **Info / success** — one-line records: `Password changed for user {Username}`, `Email sent to {To}`, HTTP request summaries.
- **Passwords are never logged** — log statements reference usernames, IPs, emails, and group names only.

**Rotation and retention:**
- Daily rolling: new file each calendar day.
- 30-day retention: oldest files auto-deleted.
- 10 MB per-file cap (rolls to `passreset-YYYYMMDD_001.log`, `_002.log`, … within the same day if exceeded).
- Shared write mode — safe for multi-worker app pools.

**Overriding the defaults** (optional — only if the IIS convention path doesn't suit you):

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": { "Microsoft.AspNetCore": "Warning" }
  },
  "WriteTo": [
    { "Name": "Console" },
    {
      "Name": "File",
      "Args": {
        "path": "D:\\Logs\\PassReset\\passreset-.log",
        "rollingInterval": "Day",
        "retainedFileCountLimit": 30,
        "fileSizeLimitBytes": 10485760,
        "rollOnFileSizeLimit": true,
        "shared": true
      }
    }
  ]
}
```

If you change `path`, ensure the app pool identity has `Modify` rights on the parent folder.

**Relationship to SIEM:** File logs serve ops troubleshooting; the `SiemSettings` syslog/email channel remains for security event forwarding. Both are independent.

---

## WebSettings

```json
"WebSettings": {
  "EnableHttpsRedirect": true,
  "UseDebugProvider": false
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `EnableHttpsRedirect` | bool | `true` | Redirects HTTP requests to HTTPS. |
| `UseDebugProvider` | bool | `false` | Bypasses AD authentication — accepts any password. **Never enable in production.** |

---

## PasswordChangeOptions

```json
"PasswordChangeOptions": {
  "UseAutomaticContext": true,
  "AllowedUsernameAttributes": [ "samaccountname" ],
  "IdTypeForUser": "UserPrincipalName",
  "PortalLockoutThreshold": 3,
  "PortalLockoutWindow": "00:30:00",
  "DefaultDomain": "yourdomain.com",
  "ClearMustChangePasswordFlag": true,
  "EnforceMinimumPasswordAge": true,
  "FailOpenOnPwnedCheckUnavailable": false,
  "AllowSetPasswordFallback": false,
  "UpdateLastPassword": false,
  "RestrictedAdGroups": [ "Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators" ],
  "AllowedAdGroups": [],
  "LdapHostnames": [ "dc01.yourdomain.com" ],
  "LdapPort": 636,
  "LdapUseSsl": true,
  "LdapUsername": "",
  "LdapPassword": ""
}
```

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `UseAutomaticContext` | bool | `true` | `true` = domain-joined server, uses machine credentials automatically. `false` = supply `LdapHostnames`, `LdapUsername`, `LdapPassword`. |
| `AllowedUsernameAttributes` | string[] | `["samaccountname"]` | AD attributes tried in order when looking up a user. Options: `samaccountname`, `userprincipalname`, `mail`. |
| `IdTypeForUser` | string | `UserPrincipalName` | How the user identity is bound after lookup. Options: `UserPrincipalName`, `SamAccountName`, `DistinguishedName`, `Sid`, `Guid`, `Name`. |
| `PortalLockoutThreshold` | int | `3` | Number of consecutive wrong-password attempts before the portal blocks further attempts (without touching AD). `0` = disabled. |
| `PortalLockoutWindow` | string | `"00:30:00"` | Duration of the portal lockout window (`hh:mm:ss`). The window is absolute — it starts at the first failure and is not reset by subsequent attempts. |
| `DefaultDomain` | string | `""` | Appended to bare usernames (e.g. `jsmith` → `jsmith@yourdomain.com`) when `IdTypeForUser` is `UserPrincipalName`. |
| `ClearMustChangePasswordFlag` | bool | `true` | Clears the "must change password at next logon" AD flag after a successful change. |
| `EnforceMinimumPasswordAge` | bool | `true` | Blocks changes before the AD minimum password age (minPwdAge) has elapsed. |
| `UpdateLastPassword` | bool | `false` | Updates the `pwdLastSet` attribute after change. Usually not required. |
| `RestrictedAdGroups` | string[] | See default | Users in these groups are blocked from changing their password. |
| `AllowedAdGroups` | string[] | `[]` | If non-empty, only users in these groups may use the tool. Leave empty to allow all users. |
| `LdapHostnames` | string[] | `[""]` | One or more hostnames or IPs of domain controllers. Used when `UseAutomaticContext` is `false`. |
| `LdapPort` | int | `636` | LDAP/LDAPS port. Default `636` (LDAPS). Use `389` for plain LDAP (not recommended). |
| `LdapUseSsl` | bool | `true` | Enables LDAPS (LDAP over TLS). Set to `false` only when LDAPS is unavailable. |
| `LdapUsername` | string | `""` | Service account UPN or SAM for LDAP bind. Used when `UseAutomaticContext` is `false`. |
| `FailOpenOnPwnedCheckUnavailable` | bool | `false` | When `true`, allows password changes to proceed when the HIBP API is unreachable (breach check is skipped). When `false`, an unreachable API blocks the change. |
| `AllowSetPasswordFallback` | bool | `false` | When `true`, falls back to the administrative `SetPassword` API on COMException. **Warning:** this may bypass AD password history enforcement. |
| `LdapPassword` | string | `""` | Password for `LdapUsername`. Store securely — consider using environment variable substitution or a secrets manager. |
| `NotificationEmailStrategy` | string | `"Mail"` | How the recipient email address is resolved for password-changed notifications. See table below. |
| `NotificationEmailDomain` | string | `""` | Domain suffix used with `SamAccountNameAtDomain` strategy. Falls back to `DefaultDomain` when empty. |
| `NotificationEmailTemplate` | string | `""` | Template string used with `Custom` strategy. Placeholders: `{samaccountname}`, `{userprincipalname}`, `{mail}`, `{defaultdomain}`. Example: `{samaccountname}@{defaultdomain}` |

### `PasswordChangeOptions.LocalPolicy`

Optional operator-managed offline policy enforcement. See
[docs/LocalPasswordPolicy-Setup.md](LocalPasswordPolicy-Setup.md) for the
full guide.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `BannedWordsPath` | string or null | `null` | Absolute path to a UTF-8 plaintext banned-words file. Null/empty disables the check. |
| `LocalPwnedPasswordsPath` | string or null | `null` | Absolute path to a directory of HIBP SHA-1 per-prefix files. When set, the remote HIBP API call is disabled. |
| `MinBannedTermLength` | integer | `4` | Minimum length for a banned term to be considered at load time. Must be >= 1. |

### Cross-platform LDAP provider (v2.0+)

When PassReset runs on Linux (or with `ProviderMode: "Ldap"` on Windows), it selects the cross-platform `LdapPasswordChangeProvider` backed by `System.DirectoryServices.Protocols`. Operator setup — service account creation, the "Change Password" extended-right grant, and LDAPS CA trust on Linux — is documented in [`AD-ServiceAccount-LDAP-Setup.md`](AD-ServiceAccount-LDAP-Setup.md).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `ProviderMode` | string | `"Auto"` | Provider selection. `Auto` = Windows provider on Windows, Ldap provider elsewhere. `Windows` = force the Windows provider (fails to start on non-Windows). `Ldap` = force the cross-platform provider on every host. |
| `ServiceAccountDn` | string | `""` | Distinguished Name of the LDAP bind account, e.g. `CN=svc-passreset,OU=ServiceAccounts,DC=corp,DC=example,DC=com`. Required when `ProviderMode` resolves to `Ldap`. |
| `ServiceAccountPassword` | string | `""` | Password for `ServiceAccountDn`. **Never store in config** — bind via the `PasswordChangeOptions__ServiceAccountPassword` environment variable (see [Secrets and env-var overrides](#secrets-and-env-var-overrides-stab-017)). |
| `BaseDn` | string | `""` | Search base used to locate users by the configured username attributes, e.g. `DC=corp,DC=example,DC=com`. |
| `LdapHostnames` | string[] | `[]` | Directory controller hostnames (also used by the Windows provider when `UseAutomaticContext: false`). The Ldap provider connects to the first reachable entry. |
| `LdapPort` | int | `636` | LDAPS port. Use `636` with `LdapUseSsl: true`. Non-TLS `389` is rejected by the Ldap provider. |
| `LdapUseSsl` | bool | `true` | Must be `true` for the Ldap provider — the change-password operation ships the new credential and refuses to run over plaintext. |
| `LdapTrustedCertificateThumbprints` | string[] | `[]` | SHA-1 or SHA-256 thumbprints of directory controller certificates to trust when the OS chain validation fails (typical for Linux hosts that haven't imported the domain CA). Spaces and colons are tolerated; comparison is case-insensitive. |

> Set `ProviderMode: "Ldap"` on Windows only for parity testing. In production on Windows, leave it at `Auto` — the Windows provider remains byte-for-byte identical to v1.4.2.

> Changing `ProviderMode` requires an app restart — provider selection is captured once at startup and not re-evaluated on `IOptionsMonitor` reloads.

### NotificationEmailStrategy values

| Value | Address resolved as | Example |
|-------|---------------------|---------|
| `Mail` | AD `mail` attribute (default) | `jane.doe@company.com` |
| `UserPrincipalName` | AD `userPrincipalName` attribute | `jdoe@company.com` |
| `SamAccountNameAtDomain` | `{samaccountname}@{NotificationEmailDomain}` | `jdoe@company.com` |
| `Custom` | Evaluate `NotificationEmailTemplate` | `{samaccountname}@{defaultdomain}` |

---

## SmtpSettings

```json
"SmtpSettings": {
  "Host": "smtp-relay.yourdomain.com",
  "Port": 587,
  "UseSsl": true,
  "Username": "",
  "Password": "",
  "FromAddress": "passreset@yourdomain.com",
  "FromName": "PassReset Self-Service",
  "TrustedCertificateThumbprints": []
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Host` | string | SMTP relay hostname. Leave empty to disable all outbound email. |
| `Port` | int | `587` = STARTTLS (recommended). `465` = SMTPS. `25` = unauthenticated relay. |
| `UseSsl` | bool | Enables TLS. Set `false` only for unauthenticated internal relays on port 25. |
| `Username` | string | SMTP authentication username. Leave empty for anonymous relay. |
| `Password` | string | SMTP authentication password. |
| `FromAddress` | string | Sender email address shown in notifications. |
| `FromName` | string | Sender display name shown in notifications. |
| `TrustedCertificateThumbprints` | string[] (optional) | Explicit SHA-1 (40 hex) or SHA-256 (64 hex) thumbprints of SMTP server certificates to trust when OS chain validation fails. Use only when installing the internal CA root into `LocalMachine\Root` is not feasible. Spaces and colons are tolerated; comparison is case-insensitive. Validation failures are logged with thumbprint + subject (never the full cert). Default: `[]` — system trust store only. There is no global "trust all" option by design. |

---

## EmailNotificationSettings

Sends a confirmation email to the user after a successful password change.

```json
"EmailNotificationSettings": {
  "Enabled": false,
  "Subject": "Your password has been changed",
  "BodyTemplate": "Hello {Username},\n\nYour password was changed successfully on {Timestamp} from IP address {IpAddress}.\n\nIf you did not make this change, contact IT Support immediately."
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable change notifications. Requires `SmtpSettings.Host` to be set. |
| `Subject` | string | Email subject line. |
| `BodyTemplate` | string | Email body. Supports `{Username}`, `{Timestamp}`, `{IpAddress}` placeholders. |

---

## PasswordExpiryNotificationSettings

A background service that emails users before their password expires. Scans members of `AllowedAdGroups` daily.

```json
"PasswordExpiryNotificationSettings": {
  "Enabled": false,
  "DaysBeforeExpiry": 14,
  "NotificationTimeUtc": "08:00",
  "PassResetUrl": "https://passreset.yourdomain.com",
  "ExpiryEmailSubject": "Your password will expire soon",
  "ExpiryEmailBodyTemplate": "Hello {Username},\n\nYour Active Directory password will expire in {DaysRemaining} day(s) on {ExpiryDate}.\n\nPlease change your password before it expires: {PassResetUrl}"
}
```

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable the expiry reminder service. |
| `DaysBeforeExpiry` | int | How many days before expiry to start sending reminders. |
| `NotificationTimeUtc` | string | Time of day (UTC, `HH:mm`) the daily scan runs. |
| `PassResetUrl` | string | URL of this tool, embedded in reminder emails. |
| `ExpiryEmailSubject` | string | Email subject. |
| `ExpiryEmailBodyTemplate` | string | Supports `{Username}`, `{DaysRemaining}`, `{ExpiryDate}`, `{PassResetUrl}`. |

---

## ClientSettings

Controls the UI and frontend behaviour.

```json
"ClientSettings": {
  "ApplicationTitle": "Change Account Password | Self-Service",
  "ChangePasswordTitle": "Change Account Password",
  "UseEmail": false,
  "ShowPasswordMeter": true,
  "UsePasswordGeneration": false,
  "MinimumDistance": 0,
  "PasswordEntropy": 16,
  "MinimumScore": 0,
  "AllowedUsernameAttributes": [ "samaccountname" ],
  "Recaptcha": {
    "Enabled": false,
    "SiteKey": "",
    "PrivateKey": "",
    "LanguageCode": "en"
  },
  "ValidationRegex": {
    "EmailRegex": "^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9-]+(?:\\.[a-zA-Z0-9-]+)*$",
    "UsernameRegex": ""
  },
  "ChangePasswordForm": {
    "HelpText": "If you are having trouble with this tool, please contact IT Support.",
    "UsernameLabel": "Username",
    "UsernameHelpblock": "Your organisation email address",
    "UsernameDefaultDomainHelperBlock": "Your organisation username",
    "CurrentPasswordLabel": "Current Password",
    "CurrentPasswordHelpblock": "",
    "NewPasswordLabel": "New Password",
    "NewPasswordHelpblock": "Choose a strong password.",
    "NewPasswordVerifyLabel": "Confirm New Password",
    "NewPasswordVerifyHelpblock": "",
    "ChangePasswordButtonLabel": "Change Password"
  },
  "ErrorsPasswordForm": {
    "FieldRequired": "This field is required.",
    "PasswordMatch": "Passwords do not match.",
    "UsernameEmailPattern": "Please enter a valid email address.",
    "UsernamePattern": "Please enter a valid username."
  },
  "Alerts": {
    "SuccessAlertTitle": "Password changed successfully.",
    "SuccessAlertBody": "Please note it may take a few minutes for your new password to reach all domain controllers.",
    "ErrorPasswordChangeNotAllowed": "You are not allowed to change your password. Please contact IT Support.",
    "ErrorInvalidCredentials": "Your current password is incorrect.",
    "ErrorInvalidDomain": "Invalid domain. Please check your username and try again.",
    "ErrorInvalidUser": "User account not found.",
    "ErrorCaptcha": "Could not verify you are not a robot. Please try again.",
    "ErrorFieldRequired": "Please fill in all required fields.",
    "ErrorFieldMismatch": "The new passwords do not match.",
    "ErrorComplexPassword": "The new password does not meet complexity requirements.",
    "ErrorConnectionLdap": "Could not connect to the directory. Please contact IT Support.",
    "ErrorScorePassword": "The password is not strong enough. Please choose a stronger password.",
    "ErrorDistancePassword": "The new password is too similar to your current password.",
    "ErrorPwnedPassword": "This password has been found in public breach databases. Please choose a different password.",
    "ErrorPasswordTooYoung": "Your password was changed too recently. Please wait before changing it again.",
    "ErrorRateLimitExceeded": "Too many attempts. Please wait a few minutes and try again.",
    "ErrorPwnedPasswordCheckFailed": "The password breach check service is temporarily unavailable. Please try again in a moment.",
    "ErrorPortalLockout": "Too many failed attempts. Please wait 30 minutes before trying again.",
    "ErrorApproachingLockout": "Incorrect password. Warning: one more failed attempt will temporarily lock your access to this portal."
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `ApplicationTitle` | string | Browser tab title. |
| `ChangePasswordTitle` | string | Heading shown on the form card. |
| `UseEmail` | bool | If `true`, the username field accepts an email address (legacy; prefer `AllowedUsernameAttributes`). |
| `ShowPasswordMeter` | bool | Displays a password strength indicator (zxcvbn) in the form. |
| `UsePasswordGeneration` | bool | Adds a "generate password" button to the new-password field. |
| `MinimumDistance` | int | Minimum Levenshtein distance between old and new password. `0` = disabled. Enforced client- and server-side. |
| `PasswordEntropy` | int | Entropy bits used by the password generator. |
| `MinimumScore` | int | Minimum zxcvbn score (0–4). `0` = disabled. UI feedback only — not enforced server-side. |
| `AllowedUsernameAttributes` | string[] | AD attributes the username field accepts. Options: `samaccountname`, `userprincipalname`, `mail`. Controls the helper text shown below the username field. Must match `PasswordChangeOptions.AllowedUsernameAttributes`. |

### Recaptcha

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable Google reCAPTCHA v3. |
| `SiteKey` | string | reCAPTCHA v3 site key (public). Loaded in the browser. |
| `PrivateKey` | string | reCAPTCHA v3 secret key. Used server-side only — never exposed to the client. |
| `LanguageCode` | string | reCAPTCHA widget language (e.g. `en`, `de`, `fr`). |

### ValidationRegex

| Key | Type | Description |
|-----|------|-------------|
| `EmailRegex` | string | Regex applied to the username field when `AllowedUsernameAttributes` contains only email-format attributes (`userprincipalname`, `mail`). |
| `UsernameRegex` | string | Regex applied to the username field when `samaccountname` is the sole allowed attribute. Leave empty to skip pattern validation. |

### ChangePasswordForm

All strings are optional — defaults are built into the React app. Override any key to localise or customise the form.

| Key | Description |
|-----|-------------|
| `HelpText` | Paragraph shown above the form. |
| `UsernameLabel` | Label for the username field. |
| `UsernameHelpblock` | Helper text shown below the username field when `UseEmail` is `true`. |
| `UsernameDefaultDomainHelperBlock` | Helper text shown when `samaccountname` is the accepted attribute. |
| `CurrentPasswordLabel` | Label for the current-password field. |
| `NewPasswordLabel` | Label for the new-password field. |
| `NewPasswordHelpblock` | Helper text below the new-password field. |
| `NewPasswordVerifyLabel` | Label for the confirm-password field. |
| `ChangePasswordButtonLabel` | Submit button text. |

### ErrorsPasswordForm

Client-side validation messages (shown before the form is submitted).

| Key | Description |
|-----|-------------|
| `FieldRequired` | Shown when a required field is empty. |
| `PasswordMatch` | Shown when new password and confirmation do not match. |
| `UsernameEmailPattern` | Shown when the username fails the email regex. |
| `UsernamePattern` | Shown when the username fails the username regex. |

### Alerts

Server error and success messages returned from the API. All keys are optional; built-in defaults are shown in the JSON example above.

| Key | Description |
|-----|-------------|
| `SuccessAlertTitle` | Heading on the success card. |
| `SuccessAlertBody` | Body text on the success card. |
| `ErrorInvalidCredentials` | Wrong current password. |
| `ErrorInvalidUser` | Username not found in AD. |
| `ErrorPasswordChangeNotAllowed` | User is in a restricted group. |
| `ErrorInvalidDomain` | Domain portion of the username is not recognised. |
| `ErrorCaptcha` | reCAPTCHA verification failed. |
| `ErrorComplexPassword` | New password does not meet AD complexity rules. |
| `ErrorConnectionLdap` | Could not reach a domain controller. |
| `ErrorScorePassword` | Password zxcvbn score is below `MinimumScore`. |
| `ErrorDistancePassword` | New password is too similar to the current one. |
| `ErrorPwnedPassword` | Password found in HIBP breach database. |
| `ErrorPasswordTooYoung` | AD minimum password age has not elapsed. |
| `ErrorRateLimitExceeded` | Built-in rate limiter (5 req / 5 min) triggered. |
| `ErrorPwnedPasswordCheckFailed` | HIBP API was unreachable; change was blocked. |
| `ErrorPortalLockout` | Portal lockout threshold reached; AD not contacted. |
| `ErrorApproachingLockout` | Wrong password and one more attempt will trigger portal lockout. |

---

### Branding (FEAT-001)

Operator branding overrides the default header (lock icon + "PassReset"), favicon, and helpdesk
hints. **Omitting the entire `Branding` block preserves the v1.2.3 default look.**

Asset files (logo, favicon) live outside the deploy directory so upgrades never overwrite them.
Default location: `C:\ProgramData\PassReset\brand\` (created by `Install-PassReset.ps1`).
The installer never removes or overwrites this directory on upgrade.

```json
"ClientSettings": {
  "Branding": {
    "CompanyName": "Contoso Ltd.",
    "PortalName": "Account Self-Service",
    "HelpdeskUrl": "https://helpdesk.contoso.com",
    "HelpdeskEmail": "helpdesk@contoso.com",
    "UsageText": "Use your corporate username and current password to choose a new password.",
    "LogoFileName": "contoso-logo.svg",
    "FaviconFileName": "favicon.ico",
    "AssetRoot": null
  }
}
```

| Key | Purpose |
|-----|---------|
| `CompanyName` | Displayed alongside the portal name in the header (optional). |
| `PortalName` | Header text. Defaults to `PassReset` when omitted. |
| `HelpdeskUrl` | Renders as a `target="_blank" rel="noopener"` link in the helpdesk block. |
| `HelpdeskEmail` | Renders as a `mailto:` link in the helpdesk block. |
| `UsageText` | Short paragraph rendered above the form. Replaces the default help text when set. |
| `LogoFileName` | File name (not full path) inside the brand asset root, served as `/brand/<file>`. Falls back to the default lock icon if the file fails to load. |
| `FaviconFileName` | File name inside the brand asset root, injected at runtime as `<link rel="icon">`. |
| `AssetRoot` | Override for the brand asset directory. Default: `C:\ProgramData\PassReset\brand\`. Use only when you need to host assets on a UNC path or alternate drive. |

The helpdesk block is hidden when both `HelpdeskUrl` and `HelpdeskEmail` are absent.

### Clipboard Clearing (FEAT-003)

When the built-in password generator writes a generated password to the clipboard, PassReset schedules an automatic clipboard wipe after a configurable delay. The clear fires **only if the clipboard content still matches the generated password** — so it never clobbers anything the user copied between generation and clear.

```json
"ClientSettings": {
  "ClipboardClearSeconds": 30
}
```

| Key | Description |
| --- | --- |
| `ClipboardClearSeconds` | Seconds after copy before the clipboard is auto-cleared. Default `30`. Set to `0` to disable the feature entirely (no timer starts and no clipboard read/write occurs). |

**Browser permission prompt:** The readback-guard uses `navigator.clipboard.readText()`. Chromium-based browsers permit this silently from an active tab; Firefox and Safari prompt the user the first time. This prompt is expected and intentional — denying it simply means the clear becomes a silent no-op, which is safe. If the Clipboard API is unavailable (insecure context, older browsers), the helper no-ops silently.

**Regeneration:** Clicking the generate button again cancels any pending clear and starts a fresh countdown against the new password.

---

## SiemSettings

Forwards security events to a SIEM via RFC 5424 syslog and/or email alerts. Both channels are opt-in; all keys are optional.

```json
"SiemSettings": {
  "Syslog": {
    "Enabled": false,
    "Host": "siem.yourdomain.com",
    "Port": 514,
    "Protocol": "UDP",
    "Facility": 10,
    "AppName": "PassReset"
  },
  "AlertEmail": {
    "Enabled": false,
    "Recipients": [ "security@yourdomain.com" ],
    "AlertOnEvents": [ "PortalLockout", "InvalidCredentials" ]
  }
}
```

### Syslog

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Set to `true` to enable syslog forwarding. |
| `Host` | string | `""` | Hostname or IP of the syslog collector / SIEM. |
| `Port` | int | `514` | UDP/TCP port of the syslog collector. |
| `Protocol` | string | `"UDP"` | Transport: `UDP` or `TCP`. TCP uses RFC 6587 octet-counting framing. |
| `Facility` | int | `10` | RFC 5424 facility number. `10` = authpriv (security/auth). Common values: `4`=auth, `16`–`23`=local0–local7. |
| `AppName` | string | `"PassReset"` | APP-NAME field in the syslog header. |

Each syslog message follows RFC 5424 format with a structured-data element:
```
<priority>1 <timestamp> <hostname> PassReset - - - [PassReset@0 event="..." user="..." ip="..."]
```

### AlertEmail

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `Enabled` | bool | `false` | Set to `true` to enable email alerts. Requires `SmtpSettings.Host` to be set. |
| `Recipients` | string[] | `[]` | One or more recipient email addresses for alert messages. |
| `AlertOnEvents` | string[] | `["PortalLockout"]` | Event type names that trigger an email alert. |

### AlertOnEvents — valid event type names

| Event | When it fires |
|-------|---------------|
| `PasswordChanged` | Password changed successfully |
| `InvalidCredentials` | Wrong current password supplied |
| `UserNotFound` | Username not found in AD |
| `PortalLockout` | Portal lockout threshold reached |
| `ApproachingLockout` | One more wrong attempt will trigger portal lockout |
| `RateLimitExceeded` | Request rejected by the rate limiter (5 req / 5 min) |
| `RecaptchaFailed` | reCAPTCHA v3 validation failed |
| `ChangeNotPermitted` | User blocked by AD group allow/block list |
| `ValidationFailed` | Request rejected by model validation |
| `Generic` | Unexpected server-side error |

---

## Section notes (formerly inline in template)

As of v1.4.0 the production template (`appsettings.Production.template.json`) is **pure JSON** — no `//` comments. The inline notes that previously lived in the template are preserved verbatim below so operators retain the historical guidance.

### Logging

> By default logs go to `%SystemDrive%\inetpub\logs\PassReset\passreset-YYYYMMDD.log`.
> Override `path` in `WriteTo[File].Args` to change location. The IIS AppPool identity
> (`IIS AppPool\PassReset` by default) must have Modify rights on the parent folder —
> the installer grants this automatically.

(Full rotation/retention behaviour is documented in [Serilog (file logging)](#serilog-file-logging) above.)

### Branding (FEAT-001)

> Operator branding (FEAT-001).
> Omit the entire `"Branding"` block to keep the v1.2.3 default look
> (LockPersonIcon + "PassReset"). Asset files (logo, favicon) live in
> `C:\ProgramData\PassReset\brand\` by default — set `"AssetRoot"` to override.

(Per-field semantics for `CompanyName`, `PortalName`, `HelpdeskUrl`, `HelpdeskEmail`, `UsageText`, `LogoFileName`, `FaviconFileName`, `AssetRoot` are documented in the Branding section above.)

### Why pure JSON?

The template now validates against [`appsettings.schema.json`](../src/PassReset.Web/appsettings.schema.json) (JSON Schema Draft 2020-12). PowerShell `Test-Json`, `System.Text.Json`, and the installer's pre-flight validator all reject JSONC (`//` comments). Keeping the template pure JSON unblocks automated schema validation in CI, during `Install-PassReset.ps1` upgrades, and inside the runtime's startup validators.

---

### Secrets and env-var overrides (STAB-017)

The three production secrets below can be sourced from process environment variables (or `dotnet user-secrets` in Development) instead of being stored in `appsettings.Production.json`. ASP.NET Core's default host builder wires `AddEnvironmentVariables()` using the `__` (double underscore) path delimiter (D-16 — no custom `PASSRESET_` prefix).

| Config key | Env var name | Source precedence |
|---|---|---|
| `SmtpSettings.Password` | `SmtpSettings__Password` | appsettings < user-secrets (Dev) < env var |
| `PasswordChangeOptions.ServiceAccountPassword` | `PasswordChangeOptions__ServiceAccountPassword` | same |
| `ClientSettings.Recaptcha.PrivateKey` | `ClientSettings__Recaptcha__PrivateKey` | same |

See `docs/Secret-Management.md` for developer (`dotnet user-secrets`) and operator (`appcmd`) workflows, and `docs/IIS-Setup.md` for the AppPool env-var snippet. The installer itself does not set these values — operators own secret injection after install (D-18). Encrypted-at-rest storage (DPAPI / Key Vault) is scheduled for v2.0 (V2-003).
