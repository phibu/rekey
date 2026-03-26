# ReKey V2 — Active Directory Service Account Setup

This guide configures the AD service account ReKey uses to authenticate against the domain and reset user passwords.

> **Only required when `UseAutomaticContext: false`.**
> If the server is domain-joined and you leave `UseAutomaticContext: true`, ReKey runs as the IIS app pool identity — see the [note at the end](#note--useautomaticcontext-true).

---

## Step 1 — Create the service account

In **Active Directory Users and Computers** or via PowerShell:

```powershell
New-ADUser `
    -Name              "ReKey Service" `
    -SamAccountName    "svc-rekey" `
    -UserPrincipalName "svc-rekey@yourdomain.com" `
    -Path              "OU=Service Accounts,DC=yourdomain,DC=com" `
    -AccountPassword   (Read-Host -AsSecureString "Password") `
    -PasswordNeverExpires $true `
    -CannotChangePassword $true `
    -Enabled           $true
```

Record the password — you will put it in `appsettings.Production.json`.

---

## Step 2 — Delegate Reset Password on the target OU

ReKey needs to reset passwords for users. Delegate this on the OU(s) that contain your user accounts.

### Via the ADUC wizard

1. Open ADUC → right-click the OU → **Delegate Control…**
2. Add `svc-rekey`
3. Choose **Create a custom task to delegate**
4. Scope: **Only the following objects** → check **User objects**
5. Permissions — check both:
   - ✓ Reset Password
   - ✓ Read and write `pwdLastSet`
   - ✓ Read and write `lockoutTime` *(optional, for a future unlock feature)*

### Via PowerShell (`dsacls`)

```powershell
$ou  = "OU=Users,DC=yourdomain,DC=com"
$sid = (Get-ADUser svc-rekey).SID

# Reset password
dsacls $ou /G "$($sid):CA;Reset Password;User"

# Read/write pwdLastSet (needed to clear the must-change-at-next-logon flag)
dsacls $ou /G "$($sid):RPWP;pwdLastSet;User"
```

---

## Step 3 — Verify read access to user attributes

ReKey reads these attributes: `mail`, `memberOf`, `pwdLastSet`, `userAccountControl`, `distinguishedName`, `sAMAccountName`, `userPrincipalName`.

These are readable by all authenticated users by default. Verify with:

```powershell
Get-ADUser <test-user> -Properties mail, memberOf, pwdLastSet `
    -Credential (Get-Credential DOMAIN\svc-rekey)
```

If that returns values, no extra delegation is needed. If your domain has tightened default read permissions, delegate **Read** on the above attributes for the target OU via ADSI Edit or `dsacls`.

---

## Step 4 — Configure `appsettings.Production.json`

```json
{
  "PasswordChangeOptions": {
    "UseAutomaticContext": false,
    "LdapHostnames":  [ "dc01.yourdomain.com", "dc02.yourdomain.com" ],
    "LdapPort":       389,
    "LdapUsername":   "YOURDOMAIN\\svc-rekey",
    "LdapPassword":   "<password from step 1>",
    "DefaultDomain":  "yourdomain.com"
  }
}
```

For LDAPS (port 636), ensure the DC certificate is trusted by the server and change `LdapPort` to `636`.

---

## Step 5 (Recommended) — Use a Group Managed Service Account (gMSA)

gMSA passwords are managed automatically by AD — no stored password needed.
Requires Windows Server 2012 domain functional level or higher.

```powershell
# Create gMSA (run once per domain, requires KDS root key)
New-ADServiceAccount `
    -Name              "svc-rekey" `
    -DNSHostName       "rekey.yourdomain.com" `
    -PrincipalsAllowedToRetrieveManagedPassword "ReKey-Servers"  # AD group of IIS servers

# Install on the ReKey IIS server
Install-ADServiceAccount svc-rekey

# Verify
Test-ADServiceAccount svc-rekey
```

In IIS, set the app pool identity to `YOURDOMAIN\svc-rekey$` (trailing `$`).
Leave the password field blank — IIS retrieves it automatically.

With gMSA, `UseAutomaticContext` can remain `true` on a domain-joined server.

---

## Note — `UseAutomaticContext: true` (domain-joined server)

When the server is domain-joined and `UseAutomaticContext: true`, ReKey authenticates as the IIS app pool identity.

| App pool identity | Authenticates to AD? | Notes |
|---|---|---|
| `ApplicationPoolIdentity` (virtual) | ❌ No | Cannot authenticate to AD directly |
| Named domain account | ✅ Yes | Requires Steps 1–3 above |
| gMSA (`svc-rekey$`) | ✅ Yes | Recommended — no stored password |

**Recommendation for production:**
Domain-joined server + gMSA + `UseAutomaticContext: true` = simplest and most secure.
