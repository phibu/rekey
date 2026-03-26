# PassReset — IIS Setup Guide

Step-by-step instructions for deploying PassReset on **Windows Server 2019 / 2022 / 2025** with **IIS 10**.

---

## Prerequisites Overview

| Requirement | Version | Notes |
|---|---|---|
| Windows Server | 2019 / 2022 / 2025 | All editions supported |
| IIS | 10 | Included with Windows Server |
| .NET Hosting Bundle | 10.x | Installs ASP.NET Core Module for IIS |
| Node.js | 20 LTS+ | Build machine only — not needed on server |
| .NET SDK | 10.x | Build machine only |
| SSL Certificate | Any trusted CA | Self-signed works for internal use |
| Active Directory | Windows Server 2016+ domain | Domain-joined server recommended |

---

## Step 1 — Enable IIS

### Via Server Manager

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

### Via PowerShell (faster)

```powershell
Install-WindowsFeature -Name `
    Web-Server, Web-WebServer, Web-Static-Content, Web-Default-Doc,
    Web-Http-Errors, Web-Http-Logging, Web-Filtering, Web-Mgmt-Console `
    -IncludeManagementTools
```

> **Note:** `Web-ASPNET45` and `Web-Asp-Net45` are .NET Framework 4.x features. They are **not** required for ASP.NET Core and do not exist on Server 2019+. Do not include them.

---

## Step 2 — Install the .NET 10 Hosting Bundle

The Hosting Bundle installs the ASP.NET Core runtime **and** the IIS in-process hosting module. It is **required** — do not install the runtime-only package.

### Option A — Direct download

Download the current Windows Hosting Bundle installer directly:

**https://dotnet.microsoft.com/permalink/dotnetcore-current-windows-runtime-bundle-installer**

Run the installer as Administrator, then run `iisreset`.

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

On the **IIS server**, run as **Administrator**:

```powershell
# Minimal — ApplicationPoolIdentity, no HTTPS binding yet
.\deploy\Install-PassReset.ps1

# Recommended — service account + certificate
.\deploy\Install-PassReset.ps1 `
    -SiteName        "PassReset" `
    -AppPoolName      "PassResetPool" `
    -PhysicalPath     "C:\inetpub\PassReset" `
    -PublishFolder    ".\publish" `
    -HttpsPort        443 `
    -CertThumbprint   "PASTE_THUMBPRINT_HERE" `
    -AppPoolIdentity  "YOURDOMAIN\svc-passreset" `
    -AppPoolPassword  (Read-Host 'App pool password' -AsSecureString)
```

The installer:
- Verifies .NET 10 Hosting Bundle and IIS features
- Creates the app pool (No Managed Code, AlwaysRunning)
- Copies files with robocopy
- Grants NTFS permissions
- Configures the HTTPS binding
- Writes a starter `appsettings.Production.json`

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
    "DefaultDomain": "yourdomain.com",
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
    "UseEmail": true,
    "ShowPasswordMeter": true,
    "Recaptcha": {
      "Enabled": false,
      "SiteKey": "",
      "PrivateKey": ""
    }
  }
}
```

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
2. **Firewall** — Allow inbound TCP 443 from the intended user subnet.
3. **HTTP → HTTPS redirect** — The installer removes the HTTP :80 binding when a certificate is supplied. If you need an HTTP redirect for external users, add it back manually in IIS → HTTP Redirect.

---

## Step 10 — Verify the Deployment

```powershell
# Health check (run from any machine with network access)
Invoke-WebRequest https://passreset.yourdomain.com/health -UseBasicParsing

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

*For AD service account setup and required permissions, see [`AD-ServiceAccount-Setup.md`](AD-ServiceAccount-Setup.md).*
