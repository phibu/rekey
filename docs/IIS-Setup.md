# PassReset — IIS Setup Guide

Step-by-step instructions for deploying PassReset on **Windows Server 2019 / 2022 / 2025** with **IIS 10**.

> **PassReset v2.0+ supports Linux deployment without IIS.** The cross-platform LDAP provider runs on any `net10.0` host (Linux, macOS, Docker). See [`AD-ServiceAccount-LDAP-Setup.md`](AD-ServiceAccount-LDAP-Setup.md) for the non-Windows path. This guide remains the canonical reference for Windows / IIS deployments.

---

## Prerequisites Overview

| Requirement | Version | Notes |
|---|---|---|
| Windows Server | 2019 / 2022 / 2025 | All editions supported |
| IIS | 10 | Included with Windows Server |
| .NET Hosting Bundle | 10.x | Installs ASP.NET Core Module for IIS |
| **PowerShell 7+** | **7.0 or later** | **Required for `Install-PassReset.ps1` (Test-Json -SchemaFile). Windows PowerShell 5.1 is NOT supported.** |
| Node.js | 20 LTS+ | Build machine only — not needed on server |
| .NET SDK | 10.x | Build machine only |
| SSL Certificate | Any trusted CA | Self-signed works for internal use |
| Active Directory | Windows Server 2016+ domain | Domain-joined server recommended |

### PowerShell 7 (required for Install-PassReset.ps1)

`Install-PassReset.ps1` uses `Test-Json -SchemaFile` to pre-validate `appsettings.Production.json`
against the shipped `appsettings.schema.json` on every upgrade. That parameter set is only
available in **PowerShell 7+**. Windows PowerShell 5.1 is NOT supported.

Install PowerShell 7 from the Microsoft Store, or via winget:

```powershell
winget install Microsoft.PowerShell
```

Always invoke the installer from `pwsh.exe`, not `powershell.exe`:

```powershell
pwsh -ExecutionPolicy Bypass -File .\Install-PassReset.ps1
```

Running under Windows PowerShell 5.1 fails immediately at the `#Requires -Version 7.0` line —
no partial install is possible.

### Windows Event Log source

The installer registers Windows Event Log source `PassReset` (under the **Application** log)
during the prerequisites phase via `[System.Diagnostics.EventLog]::CreateEventSource('PassReset', 'Application')`.
This source is used by the ASP.NET Core host to surface startup configuration validation failures
under **event ID 1001**.

The registration is idempotent — re-running the installer on an already-configured host is a
no-op. If registration fails (e.g. UAC interruption, non-admin elevation), startup validation
still causes the host to throw, but the failure will not appear in Event Viewer. Re-run
`Install-PassReset.ps1` from an elevated `pwsh` session to retry registration.

To troubleshoot a 502 after upgrade or config edit, filter Event Viewer by source `PassReset`
event ID 1001 — see [`appsettings-Production.md`](appsettings-Production.md#diagnosing-a-502-after-upgrade-or-config-edit).

---

## Step 1 — Enable IIS

### Automatic dependency install (recommended)

Run `Install-PassReset.ps1` from an elevated PowerShell. The installer detects missing IIS roles and offers a **single Y/N prompt** to enable all of them via **DISM** in one step (per-feature prompts are deliberately avoided). The required set is:

- `Web-Server`, `Web-WebServer`, `Web-Static-Content`, `Web-Default-Doc`
- `Web-Http-Errors`, `Web-Http-Logging`, `Web-Filtering`, `Web-Mgmt-Console`

> `Web-ASPNET45` / `Web-Asp-Net45` / `Web-Net-Ext45` are .NET Framework 4.x features. They are **not** required for ASP.NET Core and do not exist on Server 2019+. The installer does NOT request them.

If you decline the prompt, the installer prints the exact `dism /online /enable-feature /featurename:<X> /all /norestart` commands for each missing feature and exits cleanly (exit 0). No partial state.

Use `-Force` for unattended / CI installs — DISM enable runs without prompting.

The **.NET 10 Hosting Bundle** is **not** auto-installed. If it is missing, the installer prints the Microsoft download URL and exits 0 cleanly — see [Step 2](#step-2--install-the-net-10-hosting-bundle) below, then re-run the installer.

### Manual fallback (declined Y/N prompt, pre-Windows-Server-2019, or air-gapped builds)

If you prefer to enable features manually — for example when the installer's DISM prompt is declined, the host runs pre-Server-2019, or the deployment is air-gapped — use one of the following:

#### Via Server Manager

1. Open **Server Manager** → **Add Roles and Features**
2. Role-based or feature-based installation → next
3. Select **Web Server (IIS)** → Add Features
4. Under **Web Server → Application Development**, check:
   - ✓ .NET Extensibility 4.8
   - ✓ ASP.NET 4.8
   - ✓ ISAPI Extensions
   - ✓ ISAPI Filters
5. Under **Web Server → Common HTTP Features**, check:
   - ✓ Default Document
   - ✓ Static Content
   - ✓ HTTP Errors
6. Under **Web Server → Health and Diagnostics**, check:
   - ✓ HTTP Logging
7. Under **Web Server → Security**, check:
   - ✓ Request Filtering
8. Under **Management Tools**, check:
   - ✓ IIS Management Console
9. Complete the wizard and reboot if prompted.

#### Via PowerShell (faster)

```powershell
Install-WindowsFeature -Name `
    Web-Server, Web-WebServer, Web-Static-Content, Web-Default-Doc,
    Web-Http-Errors, Web-Http-Logging, Web-Filtering, Web-Mgmt-Console `
    -IncludeManagementTools
```

#### Via DISM (matches the installer's flow)

```powershell
foreach ($f in 'Web-Server','Web-WebServer','Web-Static-Content','Web-Default-Doc',
                'Web-Http-Errors','Web-Http-Logging','Web-Filtering','Web-Mgmt-Console') {
    dism /online /enable-feature /featurename:$f /all /norestart
}
```

> **Note:** `Web-ASPNET45` and `Web-Asp-Net45` are .NET Framework 4.x features. They are **not** required for ASP.NET Core and do not exist on Server 2019+. Do not include them.

---

## Step 2 — Install the .NET 10 Hosting Bundle

The Hosting Bundle installs the ASP.NET Core runtime **and** the IIS in-process hosting module. It is **required** — do not install the runtime-only package.

### Option A — Direct download

Download the .NET 10 Windows Hosting Bundle from the Microsoft portal:

- Landing page: **https://dotnet.microsoft.com/download/dotnet/10.0**
- Or the permalink to the current runtime bundle installer: **https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer**

Choose **"ASP.NET Core Runtime – Hosting Bundle"** for Windows. Run the installer as Administrator, then run `iisreset`.

### Option B — winget (Windows Package Manager)

```powershell
winget install Microsoft.DotNet.HostingBundle.10
```

> If winget is not available (older Server installs), install it via the [App Installer](https://aka.ms/getwinget) or use Option A.

### Verify installation

```powershell
dotnet --list-runtimes
# Should show: Microsoft.AspNetCore.App 10.x.x
```

> **Important:** If you install the Hosting Bundle after IIS is already running, IIS must be restarted (`iisreset`) for the ASP.NET Core Module to register correctly.

---

## Step 3 — Obtain an SSL Certificate

PassReset requires HTTPS. Choose one of the options below.

### Option A — Internal CA / Active Directory Certificate Services

If your organisation has AD CS:

1. Open **IIS Manager** → select the server node → **Server Certificates**
2. Click **Create Domain Certificate** in the Actions pane
3. Fill in Common Name (e.g. `passreset.yourdomain.com`), Organisation, etc.
4. Click **Select...** and choose your internal CA
5. Give it a friendly name (e.g. `PassReset`) → Finish

### Option B — Commercial Certificate (DigiCert, Sectigo, etc.)

1. In IIS Manager → Server Certificates → **Create Certificate Request**
2. Fill in the certificate details; choose **2048-bit** RSA minimum
3. Submit the CSR to your CA and download the certificate
4. Back in IIS Manager → **Complete Certificate Request** → browse to the `.cer` file

### Option C — Self-Signed (internal/testing only)

```powershell
# Creates a self-signed cert valid for 2 years, stored in LocalMachine\My
$cert = New-SelfSignedCertificate `
    -DnsName "passreset.yourdomain.com" `
    -CertStoreLocation "cert:\LocalMachine\My" `
    -NotAfter (Get-Date).AddYears(2) `
    -FriendlyName "PassReset Self-Signed"

Write-Host "Thumbprint: $($cert.Thumbprint)"
```

Copy the thumbprint — you will need it for the installer.

### Option D — Let's Encrypt (public-facing deployments only)

Use [win-acme](https://www.win-acme.com/) for automated Let's Encrypt certificates on Windows/IIS:

```powershell
# Download win-acme and run
.\wacs.exe --target iis --siteid <site-id> --installation iis
```

> Let's Encrypt requires the server to be publicly reachable on port 80/443. Not suitable for internal-only deployments.

---

## Step 4 — Find Your Certificate Thumbprint

If the certificate is already in the store:

```powershell
# List all certificates in LocalMachine\My with friendly names
Get-ChildItem Cert:\LocalMachine\My |
    Select-Object FriendlyName, Subject, Thumbprint, NotAfter |
    Format-Table -AutoSize
```

Note the **Thumbprint** of the PassReset certificate — you will pass it to `Install-PassReset.ps1`.

---

## Step 5 — Build and Publish

On your **build machine** (needs .NET 10 SDK + Node 20):

```powershell
# From the repo root
.\deploy\Publish-PassReset.ps1
```

This runs `npm ci && npm run build` (frontend) then `dotnet publish` into `deploy\publish\`.

Copy the `deploy\publish\` folder to the IIS server (or run the publish directly on the server if the SDK is installed there).

---

## Step 6 — Run the Installer

On the **IIS server**, run as **Administrator** from a **PowerShell 7+ (`pwsh`)** session:

```powershell
# Minimal — ApplicationPoolIdentity, no HTTPS binding yet
pwsh -File .\deploy\Install-PassReset.ps1

# Recommended — service account + certificate + secrets as env vars
pwsh -File .\deploy\Install-PassReset.ps1 `
    -SiteName        "PassReset" `
    -AppPoolName      "PassResetPool" `
    -PhysicalPath     "C:\inetpub\PassReset" `
    -PublishFolder    ".\publish" `
    -HttpsPort        443 `
    -CertThumbprint   "PASTE_THUMBPRINT_HERE" `
    -AppPoolIdentity  "YOURDOMAIN\svc-passreset" `
    -AppPoolPassword  (Read-Host 'App pool password' -AsSecureString) `
    -LdapPassword     (Read-Host 'LDAP password' -AsSecureString) `
    -SmtpPassword     (Read-Host 'SMTP password' -AsSecureString)

# Upgrading an existing installation (unattended — skip confirmation prompt)
.\deploy\Install-PassReset.ps1 -Force -CertThumbprint "PASTE_THUMBPRINT_HERE"
```

The installer:
- Verifies .NET 10 Hosting Bundle and IIS features
- Creates the app pool (No Managed Code, AlwaysRunning)
- Copies files with robocopy
- Grants NTFS permissions
- Configures the HTTPS binding
- Writes a starter `appsettings.Production.json` (skipped if the file already exists)
- Stores secrets as IIS app pool environment variables (existing values are never overwritten)

**Upgrading:** when an existing PassReset site is detected the installer displays the installed and incoming version numbers, prompts for confirmation, and creates a dated backup of the current deployment (e.g. `C:\inetpub\PassReset_backup_20260327-1430\`) before overwriting. Pass `-Force` to skip the confirmation for unattended deployments.

---

## Step 7 — Configure the Application

Edit `C:\inetpub\PassReset\appsettings.Production.json`:

```json
{
  "WebSettings": {
    "EnableHttpsRedirect": true,
    "UseDebugProvider": false
  },
  "PasswordChangeOptions": {
    "UseAutomaticContext": true,
    "AllowedUsernameAttributes": [ "samaccountname" ],
    "DefaultDomain": "yourdomain.com",
    "PortalLockoutThreshold": 3,
    "PortalLockoutWindow": "00:30:00",
    "ClearMustChangePasswordFlag": true,
    "EnforceMinimumPasswordAge": true
  },
  "SmtpSettings": {
    "Host": "smtp-relay.yourdomain.com",
    "Port": 587,
    "UseSsl": true,
    "FromAddress": "passreset@yourdomain.com",
    "FromName": "PassReset Self-Service"
  },
  "ClientSettings": {
    "AllowedUsernameAttributes": [ "samaccountname" ],
    "ShowPasswordMeter": true,
    "Recaptcha": {
      "Enabled": false,
      "SiteKey": "",
      "PrivateKey": ""
    }
  }
}
```

> The starter `appsettings.Production.json` written by the installer contains all available keys with defaults. The snippet above shows the keys most commonly changed. See [`appsettings-Production.md`](appsettings-Production.md) for the full reference.

---

## Step 8 — Test with Debug Provider

Before going live, enable the debug provider to verify IIS is serving the app correctly:

1. Set `"UseDebugProvider": true` in `appsettings.Production.json`
2. Browse to `https://passreset.yourdomain.com`
3. Test a password change with username `invalidCredentials` — you should see the expected error
4. Set `"UseDebugProvider": false` when ready for production

---

## Step 9 — DNS and Firewall

1. **DNS** — Create an A record (or CNAME) pointing `passreset.yourdomain.com` to the server IP.
2. **Firewall** — Allow inbound TCP 443 **and TCP 80** from the intended user subnet. Port 80 is required for the HTTP→HTTPS redirect to reach the application.
3. **HTTP → HTTPS redirect** — The installer keeps the HTTP :80 binding by default so that ASP.NET Core's `UseHttpsRedirection()` middleware can issue a 301 redirect. To disable HTTP entirely (no redirect, HTTPS only), pass `-HttpPort 0` to the installer.

---

## Step 10 — Verify the Deployment

```powershell
# Health check (run from any machine with network access)
Invoke-WebRequest https://passreset.yourdomain.com/api/health -UseBasicParsing

# Expected: StatusCode 200, Content: Healthy
```

---

## Troubleshooting

### 502.5 / 500.30 — Application failed to start

- Run `iisreset` after installing the .NET 10 Hosting Bundle.
- Check the Windows Event Viewer → **Application** log for `IIS AspNetCore Module` errors.
- Ensure the app pool identity has **ReadAndExecute** permission on `C:\inetpub\PassReset\`.

### 403 Forbidden on static files

- Check that `Web-Static-Content` IIS feature is installed.
- Verify the `wwwroot\` folder exists and contains `index.html` (run `Publish-PassReset.ps1` first).

### Password change fails immediately

- Verify the server is domain-joined (`systeminfo | findstr /i "domain"`).
- Check `UseAutomaticContext` setting.
- Review PassReset logs under `C:\inetpub\PassReset\logs\` or Windows Event Log.
- Test with `UseDebugProvider: true` to isolate AD vs application issues.

### Certificate not found in IIS binding

- Ensure the certificate is in **LocalMachine\My** (not CurrentUser\My).
- Run `Get-ChildItem Cert:\LocalMachine\My` to confirm.
- Re-run the installer with the correct thumbprint.

### ERR_CONNECTION_REFUSED on HTTP

The HTTP :80 binding is missing from the IIS site. This happens if the site was installed with an older version of the installer (which removed :80) or if `-HttpPort 0` was passed.

Re-add the binding:

```powershell
New-WebBinding -Name "PassReset" -Protocol http -Port 80
```

Then verify `EnableHttpsRedirect: true` is set in `appsettings.Production.json` and restart the site (`iisreset` or Recycle in IIS Manager). Browsers will now receive a 301 redirect to HTTPS.

To prevent this on a fresh install, re-run the installer — it now retains the HTTP :80 binding by default.

### HSTS / HTTPS redirect loop

- Set `"EnableHttpsRedirect": false` temporarily to diagnose.
- Ensure the HTTPS binding has a valid, trusted certificate.

---

## Certificate Renewal

Certificates must be renewed before expiry. After renewal:

1. Import the new certificate into `Cert:\LocalMachine\My`.
2. Update the IIS HTTPS binding:
   ```powershell
   $site    = "PassReset"
   $newThumb = "NEW_THUMBPRINT_HERE"
   $binding = Get-WebBinding -Name $site -Protocol https
   $binding.RemoveSslCertificate()
   $binding.AddSslCertificate($newThumb, "My")
   ```
3. No application restart is needed.

---

## Environment Variables for Secrets (STAB-017)

PassReset binds configuration from environment variables via ASP.NET Core's default `__` path delimiter. Operators may inject the three in-scope secrets — `SmtpSettings.Password`, `PasswordChangeOptions.ServiceAccountPassword`, `ClientSettings.Recaptcha.PrivateKey` — via AppPool-scoped environment variables instead of writing them to `appsettings.Production.json`.

**Set a secret via `appcmd.exe`** (full path: `%systemroot%\system32\inetsrv\appcmd.exe`):
```powershell
& "$env:windir\system32\inetsrv\appcmd.exe" set config `
    -section:applicationPools `
    "/[name='PassReset'].environmentVariables.[name='SmtpSettings__Password',value='<secret>']" `
    /commit:apphost

& "$env:windir\system32\inetsrv\appcmd.exe" set config `
    -section:applicationPools `
    "/[name='PassReset'].environmentVariables.[name='PasswordChangeOptions__ServiceAccountPassword',value='<secret>']" `
    /commit:apphost

& "$env:windir\system32\inetsrv\appcmd.exe" set config `
    -section:applicationPools `
    "/[name='PassReset'].environmentVariables.[name='ClientSettings__Recaptcha__PrivateKey',value='<secret>']" `
    /commit:apphost
```

Apply after setting:
```powershell
Restart-WebAppPool -Name 'PassReset'
# or: iisreset
```

Env-var values are scoped to the PassReset AppPool and never appear on disk outside `applicationHost.config`. The installer itself does NOT set these variables (D-18) — operators inject them after the install completes. See `docs/Secret-Management.md` for the complete STAB-017 workflow (developer user-secrets, operator AppPool env vars, and the v2.0 DPAPI/Key Vault migration path).

**HTTPS binding reminder (STAB-016):** STAB-016 wires HSTS emission through the runtime `IOptions<WebSettings>` pipeline; the installer now warns when an HTTPS binding is missing. Verify the binding with `Get-WebBinding -Name PassReset -Protocol https` before turning `EnableHttpsRedirect: true` on in production.

---

*For AD service account setup and required permissions, see [`AD-ServiceAccount-Setup.md`](AD-ServiceAccount-Setup.md).*
