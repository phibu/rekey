# Admin UI

PassReset includes a loopback-only admin website for editing configuration without hand-editing `appsettings.Production.json`. Introduced in Phase 13 (v2.0.0-alpha.3).

## Access model

The admin UI is bound to `127.0.0.1` on a dedicated Kestrel listener. It is not reachable over the public HTTPS binding and is not exposed to the network. To use it:

1. RDP or console to the PassReset server.
2. Open a browser on the server itself.
3. Navigate to `http://localhost:5010/admin` (default port; see `AdminSettings.LoopbackPort`).

Socket-level enforcement means the admin UI cannot be reached from any other host, regardless of firewall rules or IIS bindings.

## What it edits

| Section | Fields |
|---------|--------|
| LDAP | `UseAutomaticContext`, `ProviderMode`, `LdapHostnames`, `LdapPort`, `LdapUseSsl`, `BaseDn`, `ServiceAccountDn`, `DefaultDomain`, `LdapPassword`, `ServiceAccountPassword` |
| SMTP | `Host`, `Port`, `Username`, `FromAddress`, `UseStartTls`, `Password` |
| reCAPTCHA | `Enabled`, `SiteKey`, `PrivateKey` |
| Groups | `AllowedAdGroups`, `RestrictedAdGroups` |
| Local Policy | `BannedWordsPath`, `LocalPwnedPasswordsPath`, `MinBannedTermLength` |
| SIEM | `Syslog.Enabled`, `Syslog.Host`, `Syslog.Port`, `Syslog.Protocol` |

Rarely-touched settings (`AllowedUsernameAttributes`, `PortalLockoutWindow`, `PasswordExpiryNotification` schedule, logging configuration) remain in `appsettings.Production.json`. Hand-editing is still supported.

## Secret storage

The four outbound-auth secrets (`LdapPassword`, `ServiceAccountPassword`, `SmtpPassword`, `RecaptchaPrivateKey`) are stored encrypted in `secrets.dat` next to the app using ASP.NET Core Data Protection. On Windows, the Data Protection key ring is itself protected by DPAPI (machine-scoped); the `secrets.dat` file is useless on any other machine.

`appsettings.Production.json` continues to hold non-secret configuration in plaintext.

### Secret precedence

1. `appsettings.json` / `appsettings.Production.json`
2. `secrets.dat` (via `SecretConfigurationProvider`)
3. User secrets (development only)
4. **Environment variables** (STAB-017) — **highest precedence**
5. Command-line args

If you set `SmtpSettings__Password` as an env var, it wins over whatever the admin UI saved. This is the intended STAB-017 override and is unchanged.

## First install

After `Install-PassReset.ps1` completes:

1. RDP to the server.
2. Browse `http://localhost:5010/admin`.
3. Fill in the LDAP / SMTP / reCAPTCHA / groups / local policy / SIEM sections.
4. Click **Save** on each page.
5. Navigate to **Recycle** → click **Recycle App Pool**.
6. Test a password change against `https://<public-hostname>/`.

## Credential rotation

To rotate a secret (e.g., LDAP service account password):

1. Admin UI → **LDAP** → enter the new value in the password field.
2. Click **Save**.
3. Navigate to **Recycle** → click **Recycle App Pool**.

Leaving a password field blank on any page means "keep the existing value" — the current plaintext is never rendered into the form.

## Key storage

The Data Protection key ring lives in `<install-dir>\keys\` by default (override with `AdminSettings.KeyStorePath`).

**If this directory is lost, `secrets.dat` becomes unrecoverable.** Back it up as part of your server backup routine. The installer sets the ACL to allow only the IIS app pool identity and local administrators.

## Linux deployment

The Data Protection API requires either DPAPI (Windows) or certificate-based protection (non-Windows). On Linux:

1. Install a certificate to a keystore accessible to the app pool identity.
2. Set `AdminSettings.DataProtectionCertThumbprint` to that cert's SHA-1 thumbprint.

The validator fails startup if `Enabled` is true and `DataProtectionCertThumbprint` is unset on a non-Windows host.

## Disabling the admin UI

Set `AdminSettings.Enabled = false` in `appsettings.Production.json` and recycle the app pool. The loopback listener will not be started; admin routes will not be registered.

## Troubleshooting

**Admin UI unreachable at `http://localhost:5010/admin`:**
- Verify `AdminSettings.Enabled` is `true` in `appsettings.Production.json`.
- Verify the app pool is running (`Get-IISAppPool -Name PassResetPool`).
- Verify the loopback port is not bound by another process (`Get-NetTCPConnection -LocalPort 5010`).

**"This password is not allowed by local policy" unexpected after admin save:**
- The admin UI wrote Local Policy settings to `appsettings.Production.json`. Confirm the file contents look correct, then recycle the app pool.

**`secrets.dat` exists but secrets don't seem to apply:**
- Check for env-var overrides — `PasswordChangeOptions__LdapPassword` or similar. STAB-017 env vars override the admin-UI-stored value.

**CryptographicException on startup:**
- The Data Protection key ring at `<install-dir>\keys\` is unreadable or corrupt. If you moved the install between machines, DPAPI-protected keys from the old machine cannot be read on the new one. Restore the original keys directory, or re-enter all secrets via the admin UI to generate new ones.
