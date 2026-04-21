# AD Service Account Setup — LDAP Provider (v2.0+)

This guide applies when running PassReset with `PasswordChangeOptions.ProviderMode` resolving to `Ldap` — that is:
- **Any Linux deployment** (Auto → Ldap because the host isn't Windows), or
- **Any explicit `ProviderMode: "Ldap"`** (e.g. testing Linux parity on a Windows host).

On Windows with `ProviderMode: "Auto"` or `"Windows"`, follow the existing [`AD-ServiceAccount-Setup.md`](AD-ServiceAccount-Setup.md) instead — no change.

---

## 1. Create the service account

```powershell
New-ADUser `
    -Name "svc-passreset" `
    -SamAccountName "svc-passreset" `
    -UserPrincipalName "svc-passreset@corp.example.com" `
    -Path "OU=ServiceAccounts,DC=corp,DC=example,DC=com" `
    -AccountPassword (Read-Host "Password" -AsSecureString) `
    -Enabled $true `
    -PasswordNeverExpires $true
```

Note the resulting DN — you'll put it in `PasswordChangeOptions.ServiceAccountDn`.

## 2. Grant the "Change Password" extended right

The service account must have the *Change Password* extended right on every OU containing users whose passwords it needs to change. `Reset Password` is intentionally NOT granted — PassReset never bypasses the user's current password.

### UI path

1. Open **Active Directory Users and Computers**, enable *Advanced Features*.
2. Right-click the target OU → **Properties** → **Security** → **Advanced** → **Add**.
3. Principal: your service account. Type: **Allow**. Applies to: **Descendant User objects**.
4. Check only **Change password**. Click OK.

### PowerShell path

```powershell
$ou  = "OU=Users,DC=corp,DC=example,DC=com"
$svc = "svc-passreset"
$acl = Get-Acl "AD:\$ou"
$sid = (Get-ADUser $svc).SID
$changePwdGuid = [Guid]"ab721a53-1e2f-11d0-9819-00aa0040529b"  # well-known
$rule = New-Object System.DirectoryServices.ActiveDirectoryAccessRule `
    $sid, "ExtendedRight", "Allow", $changePwdGuid, "Descendents", "bf967aba-0de6-11d0-a285-00aa003049e2"
$acl.AddAccessRule($rule)
Set-Acl "AD:\$ou" -AclObject $acl
```

## 3. LDAPS certificate trust (Linux hosts)

The Ldap provider requires LDAPS (`LdapUseSsl: true`, port 636). Linux hosts do not automatically trust your domain CA.

**Option A — Install the domain CA cert into the system trust store:**

```bash
sudo cp corp-ca-root.crt /usr/local/share/ca-certificates/
sudo update-ca-certificates
```

**Option B — Pin via `LdapTrustedCertificateThumbprints`:**

```powershell
$cert = Get-ChildItem Cert:\LocalMachine\Root | Where-Object Subject -match "Corp Root CA"
$cert.Thumbprint
```

Add the returned thumbprint (SHA-1 or SHA-256 hex) to `LdapTrustedCertificateThumbprints` in `appsettings.Production.json`.

## 4. Bind the password via environment variable

Never commit the service account password to config. Set it via env var using the ASP.NET Core delimiter pattern:

```bash
export PasswordChangeOptions__ServiceAccountPassword='<the password>'
```

## 5. Troubleshooting matrix

| Symptom | LDAP ResultCode | Likely fix |
|---|---|---|
| All change attempts return `InvalidCredentials` | `InvalidCredentials` during bind | Service account DN wrong, or password env var not picked up |
| All change attempts return `ChangeNotPermitted` | `InsufficientAccessRights` on Modify | Step 2 not performed on the user's OU |
| "Password does not meet complexity" on valid passwords | `ConstraintViolation` + extendedError `0x0000052D` | Real policy issue, not a bug |
| Connection hangs for 3s then fails | Health endpoint shows `ad: unhealthy` | LDAPS cert trust failed; see step 3 |
