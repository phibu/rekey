#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs ReKey V2 on IIS (Windows Server 2022 / IIS 10).

.DESCRIPTION
    This script:
      1. Verifies prerequisites (.NET 10 Hosting Bundle, IIS, required IIS features).
      2. Creates or updates the IIS application pool and site.
      3. Copies the published application to the target folder.
      4. Sets NTFS permissions for the app pool identity.
      5. Writes a starter appsettings.Production.json with placeholders.
      6. Optionally binds an existing HTTPS certificate.

.PARAMETER SiteName
    Name of the IIS site to create or update. Default: ReKey

.PARAMETER AppPoolName
    Name of the IIS application pool. Default: ReKeyPool

.PARAMETER PhysicalPath
    Folder where the app will be deployed. Default: C:\inetpub\ReKey

.PARAMETER PublishFolder
    Path to the dotnet publish output folder (the folder containing ReKey.Web.exe).
    If omitted the script looks for a publish\ subfolder next to itself.

.PARAMETER HttpsPort
    HTTPS port to bind. Default: 443

.PARAMETER CertThumbprint
    Thumbprint of an existing certificate in LocalMachine\My to bind.
    Leave empty to skip HTTPS binding (configure manually later).

.PARAMETER AppPoolIdentity
    Service account in DOMAIN\User format.
    Leave empty to use ApplicationPoolIdentity (built-in virtual account).

.PARAMETER AppPoolPassword
    Password for AppPoolIdentity service account. Only used when AppPoolIdentity is set.

.EXAMPLE
    # Minimal — uses built-in app pool identity, no HTTPS binding wired yet:
    .\Install-ReKey.ps1

.EXAMPLE
    # Full — service account + certificate:
    .\Install-ReKey.ps1 `
        -AppPoolIdentity "CORP\svc-rekey" `
        -AppPoolPassword "S3cr3tP@ss!" `
        -CertThumbprint "A1B2C3D4E5F6..."
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SiteName        = 'ReKey',
    [string] $AppPoolName     = 'ReKeyPool',
    [string] $PhysicalPath    = 'C:\inetpub\ReKey',
    [string] $PublishFolder   = '',
    [int]    $HttpsPort       = 443,
    [string] $CertThumbprint  = '',
    [string] $AppPoolIdentity = '',
    [string] $AppPoolPassword = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─── Helpers ──────────────────────────────────────────────────────────────────

function Write-Step  { param([string]$Msg) Write-Host "`n▶ $Msg" -ForegroundColor Cyan }
function Write-Ok    { param([string]$Msg) Write-Host "  ✓ $Msg" -ForegroundColor Green }
function Write-Warn  { param([string]$Msg) Write-Host "  ⚠ $Msg" -ForegroundColor Yellow }
function Abort       { param([string]$Msg) Write-Host "`n✗ $Msg`n" -ForegroundColor Red; exit 1 }

# ─── 1. Prerequisites ─────────────────────────────────────────────────────────

Write-Step 'Checking prerequisites'

# IIS
if (-not (Get-Service -Name W3SVC -ErrorAction SilentlyContinue)) {
    Abort 'IIS (W3SVC) is not installed. Install the Web Server (IIS) role first.'
}
Write-Ok 'IIS is installed'

# Required IIS features
$requiredFeatures = @(
    'Web-Server',
    'Web-WebServer',
    'Web-Static-Content',
    'Web-Default-Doc',
    'Web-Http-Errors',
    'Web-ASPNET45',      # needed even for .NET Core / in-process hosting
    'Web-Asp-Net45',
    'Web-Http-Logging',
    'Web-Filtering',
    'Web-Mgmt-Console'
)

$missing = $requiredFeatures | Where-Object {
    (Get-WindowsFeature -Name $_).InstallState -ne 'Installed'
}

if ($missing) {
    Write-Warn "Installing missing IIS features: $($missing -join ', ')"
    Install-WindowsFeature -Name $missing -IncludeManagementTools | Out-Null
    Write-Ok 'IIS features installed'
} else {
    Write-Ok 'All required IIS features present'
}

# .NET 10 Hosting Bundle
$hostingBundle = Get-ItemProperty `
    -Path 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost' `
    -ErrorAction SilentlyContinue

if (-not $hostingBundle) {
    Abort '.NET Hosting Bundle is not installed. Download it from https://dot.net/download and run this script again.'
}

$installedRuntime = $hostingBundle.Version
if (-not ($installedRuntime -match '^10\.')) {
    Abort ".NET 10 Hosting Bundle is required but found version $installedRuntime. Please install the .NET 10 Hosting Bundle."
}
Write-Ok ".NET Hosting Bundle $installedRuntime detected"

# ─── 2. Resolve publish folder ────────────────────────────────────────────────

Write-Step 'Resolving publish output'

if (-not $PublishFolder) {
    $scriptDir     = Split-Path -Parent $MyInvocation.MyCommand.Path
    $PublishFolder = Join-Path $scriptDir 'publish'
}

if (-not (Test-Path $PublishFolder)) {
    Abort "Publish folder not found: $PublishFolder`nRun: dotnet publish src\ReKey.Web -c Release -o deploy\publish"
}

$webExe = Join-Path $PublishFolder 'ReKey.Web.exe'
if (-not (Test-Path $webExe)) {
    Abort "ReKey.Web.exe not found in $PublishFolder. Ensure you ran dotnet publish first."
}
Write-Ok "Publish folder: $PublishFolder"

# ─── 3. Create deployment folder ──────────────────────────────────────────────

Write-Step "Deploying to $PhysicalPath"

if (-not (Test-Path $PhysicalPath)) {
    New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    Write-Ok "Created $PhysicalPath"
}

# Stop the site/pool before copying so locked files are released
Import-Module WebAdministration -ErrorAction SilentlyContinue

$poolExists = Test-Path "IIS:\AppPools\$AppPoolName"
$siteExists = Test-Path "IIS:\Sites\$SiteName"

if ($poolExists) {
    $poolState = (Get-WebAppPoolState -Name $AppPoolName).Value
    if ($poolState -eq 'Started') {
        Stop-WebAppPool -Name $AppPoolName
        Write-Ok "Stopped app pool $AppPoolName"
    }
}

if ($siteExists) {
    $siteState = (Get-WebsiteState -Name $SiteName).Value
    if ($siteState -eq 'Started') {
        Stop-Website -Name $SiteName
        Write-Ok "Stopped site $SiteName"
    }
}

# Copy publish output (robocopy: /MIR = mirror, /NFL /NDL = quiet)
$rc = robocopy $PublishFolder $PhysicalPath /MIR /NFL /NDL /NJH /NJS /R:3 /W:5
if ($LASTEXITCODE -ge 8) {
    Abort "robocopy failed with exit code $LASTEXITCODE"
}
Write-Ok 'Files copied'

# ─── 4. App pool ──────────────────────────────────────────────────────────────

Write-Step "Configuring app pool: $AppPoolName"

if (-not $poolExists) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Write-Ok "Created app pool $AppPoolName"
}

# No managed code — ASP.NET Core runs in-process via the hosting module
Set-ItemProperty "IIS:\AppPools\$AppPoolName" managedRuntimeVersion ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" enable32BitAppOnWin64 $false
Set-ItemProperty "IIS:\AppPools\$AppPoolName" startMode 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$AppPoolName" autoStart $true

if ($AppPoolIdentity) {
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.userName     $AppPoolIdentity
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.password     $AppPoolPassword
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 3  # SpecificUser
    Write-Ok "App pool identity: $AppPoolIdentity"
} else {
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 4  # ApplicationPoolIdentity
    Write-Ok 'App pool identity: ApplicationPoolIdentity'
}

# ─── 5. IIS site ──────────────────────────────────────────────────────────────

Write-Step "Configuring site: $SiteName"

if (-not $siteExists) {
    New-Website `
        -Name         $SiteName `
        -PhysicalPath $PhysicalPath `
        -ApplicationPool $AppPoolName `
        -Port         80 `
        -Force | Out-Null
    Write-Ok "Created site $SiteName (HTTP :80 placeholder)"
} else {
    Set-ItemProperty "IIS:\Sites\$SiteName" physicalPath $PhysicalPath
    Set-ItemProperty "IIS:\Sites\$SiteName" applicationPool $AppPoolName
    Write-Ok "Updated site $SiteName"
}

# HTTPS binding
if ($CertThumbprint) {
    $cert = Get-ChildItem Cert:\LocalMachine\My |
            Where-Object { $_.Thumbprint -eq $CertThumbprint } |
            Select-Object -First 1

    if (-not $cert) {
        Write-Warn "Certificate with thumbprint $CertThumbprint not found in LocalMachine\My — skipping HTTPS binding."
    } else {
        # Remove existing HTTPS binding on this port if any
        $existingBinding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -ErrorAction SilentlyContinue
        if ($existingBinding) { Remove-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort }

        New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -SslFlags 0
        $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort
        $binding.AddSslCertificate($CertThumbprint, 'My')
        Write-Ok "HTTPS binding configured on port $HttpsPort"
    }
} else {
    Write-Warn 'No certificate thumbprint supplied — HTTPS binding not configured. Add it manually or re-run with -CertThumbprint.'
}

# Remove default HTTP :80 binding if HTTPS is in place (reduces attack surface)
if ($CertThumbprint) {
    $httpBinding = Get-WebBinding -Name $SiteName -Protocol http -Port 80 -ErrorAction SilentlyContinue
    if ($httpBinding) {
        Remove-WebBinding -Name $SiteName -Protocol http -Port 80
        Write-Ok 'Removed HTTP :80 binding (HTTPS only)'
    }
}

# ─── 6. NTFS permissions ──────────────────────────────────────────────────────

Write-Step 'Setting NTFS permissions'

$identity = if ($AppPoolIdentity) {
    $AppPoolIdentity
} else {
    "IIS AppPool\$AppPoolName"
}

$acl  = Get-Acl $PhysicalPath
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $identity,
    'ReadAndExecute',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow'
)
$acl.SetAccessRule($rule)
Set-Acl -Path $PhysicalPath -AclObject $acl
Write-Ok "ReadAndExecute granted to $identity on $PhysicalPath"

# Logs subfolder needs write access
$logsPath = Join-Path $PhysicalPath 'logs'
if (-not (Test-Path $logsPath)) { New-Item -ItemType Directory -Path $logsPath | Out-Null }

$aclLogs  = Get-Acl $logsPath
$ruleLogs = New-Object System.Security.AccessControl.FileSystemAccessRule(
    $identity,
    'Modify',
    'ContainerInherit,ObjectInherit',
    'None',
    'Allow'
)
$aclLogs.SetAccessRule($ruleLogs)
Set-Acl -Path $logsPath -AclObject $aclLogs
Write-Ok "Modify granted to $identity on $logsPath"

# ─── 7. Write starter production config ───────────────────────────────────────

Write-Step 'Writing starter appsettings.Production.json'

$prodConfig = Join-Path $PhysicalPath 'appsettings.Production.json'

if (Test-Path $prodConfig) {
    Write-Warn "appsettings.Production.json already exists — not overwriting. Edit it manually."
} else {
    @'
{
  "WebSettings": {
    "EnableHttpsRedirect": true,
    "UseDebugProvider": false
  },
  "PasswordChangeOptions": {
    "UseAutomaticContext": true,
    "IdTypeForUser": "UserPrincipalName",
    "DefaultDomain": "yourdomain.com",
    "ClearMustChangePasswordFlag": true,
    "EnforceMinimumPasswordAge": true,
    "RestrictedAdGroups": [ "Domain Admins", "Enterprise Admins", "Schema Admins", "Administrators" ],
    "AllowedAdGroups": [],
    "LdapHostnames": [ "" ],
    "LdapPort": 389,
    "LdapUsername": "",
    "LdapPassword": ""
  },
  "SmtpSettings": {
    "Host": "smtp-relay.yourdomain.com",
    "Port": 587,
    "UseSsl": true,
    "Username": "",
    "Password": "",
    "FromAddress": "passcore@yourdomain.com",
    "FromName": "ReKey Self-Service"
  },
  "EmailNotificationSettings": {
    "Enabled": false
  },
  "PasswordExpiryNotificationSettings": {
    "Enabled": false,
    "ReKeyUrl": "https://rekey.yourdomain.com"
  },
  "ClientSettings": {
    "UseEmail": true,
    "ShowPasswordMeter": true,
    "Recaptcha": {
      "SiteKey": "",
      "PrivateKey": ""
    }
  }
}
'@ | Set-Content -Path $prodConfig -Encoding UTF8
    Write-Ok "Written to $prodConfig — fill in your domain details before starting the site."
}

# ─── 8. Start site ────────────────────────────────────────────────────────────

Write-Step 'Starting app pool and site'

Start-WebAppPool -Name $AppPoolName
Start-Website    -Name $SiteName

Write-Ok "App pool $AppPoolName started"
Write-Ok "Site $SiteName started"

# ─── Done ─────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '══════════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host '  ReKey V2 installed successfully.' -ForegroundColor Green
Write-Host ''
Write-Host '  Next steps:' -ForegroundColor Yellow
Write-Host "  1. Edit $prodConfig"
Write-Host '     • Set DefaultDomain, SmtpSettings, Recaptcha keys, etc.'
if (-not $CertThumbprint) {
Write-Host '  2. Add an HTTPS certificate binding in IIS Manager.'
}
Write-Host '  3. Browse to the site and test with UseDebugProvider: true first.'
Write-Host '  4. Set UseDebugProvider: false when ready for production.'
Write-Host '══════════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''
