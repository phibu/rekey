# appsettings.Production.json Reference

This file overrides `appsettings.json` in production. Place it in the same folder as `PassReset.Web.exe` (e.g. `C:\inetpub\PassReset\`). The install script creates a starter copy automatically — edit it before starting the site.

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
  "IdTypeForUser": "UserPrincipalName",
  "DefaultDomain": "yourdomain.com",
  "ClearMustChangePasswordFlag": true,
  "EnforceMinimumPasswordAge": true,
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
| `IdTypeForUser` | string | `UserPrincipalName` | How users are looked up in AD. Options: `UserPrincipalName`, `SamAccountName`, `DistinguishedName`, `Sid`, `Guid`, `Name`. |
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
| `LdapPassword` | string | `""` | Password for `LdapUsername`. Store securely — consider using environment variable substitution or a secrets manager. |

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
  "FromName": "PassReset Self-Service"
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
  "UseEmail": true,
  "ShowPasswordMeter": true,
  "UsePasswordGeneration": false,
  "MinimumDistance": 0,
  "PasswordEntropy": 16,
  "MinimumScore": 0,
  "Recaptcha": {
    "Enabled": false,
    "SiteKey": "",
    "PrivateKey": "",
    "LanguageCode": "en"
  }
}
```

| Key | Type | Description |
|-----|------|-------------|
| `UseEmail` | bool | If `true`, the username field accepts an email address. |
| `ShowPasswordMeter` | bool | Displays a password strength indicator in the form. |
| `UsePasswordGeneration` | bool | Adds a "generate password" button to the form. |
| `MinimumDistance` | int | Minimum edit distance between old and new password. `0` = disabled. |
| `PasswordEntropy` | int | Minimum entropy bits for generated passwords. |
| `MinimumScore` | int | Minimum zxcvbn score (0–4) required for new passwords. `0` = disabled. |

### Recaptcha

| Key | Type | Description |
|-----|------|-------------|
| `Enabled` | bool | Set to `true` to enable Google reCAPTCHA v3. |
| `SiteKey` | string | reCAPTCHA v3 site key (public). Loaded in the browser. |
| `PrivateKey` | string | reCAPTCHA v3 secret key. Used server-side only — never exposed to the client. |
| `LanguageCode` | string | reCAPTCHA widget language (e.g. `en`, `de`, `fr`). |
