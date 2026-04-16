#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installs PassReset on IIS (Windows Server 2019 / 2022 / 2025, IIS 10).

.DESCRIPTION
    This script:
      1. Verifies prerequisites (.NET 10 Hosting Bundle, IIS, required IIS features).
      2. Creates or updates the IIS application pool and site.
      3. Copies the published application to the target folder.
      4. Sets NTFS permissions for the app pool identity.
      5. Writes a starter appsettings.Production.json with placeholders.
      6. Optionally binds an existing HTTPS certificate.

.PARAMETER SiteName
    Name of the IIS site to create or update. Default: PassReset

.PARAMETER AppPoolName
    Name of the IIS application pool. Default: PassResetPool

.PARAMETER PhysicalPath
    Folder where the app will be deployed. Default: C:\inetpub\PassReset

.PARAMETER PublishFolder
    Path to the dotnet publish output folder (the folder containing PassReset.Web.exe).
    If omitted the script looks for a publish\ subfolder next to itself.

.PARAMETER HttpsPort
    HTTPS port to bind. Default: 443

.PARAMETER HttpPort
    HTTP port to keep bound for HTTP→HTTPS redirect. Default: 80.
    Set to 0 to remove the HTTP binding entirely (HTTPS-only, no redirect).

.PARAMETER CertThumbprint
    Thumbprint of an existing certificate in LocalMachine\My to bind.
    Leave empty to skip HTTPS binding (configure manually later).

.PARAMETER AppPoolIdentity
    Service account in DOMAIN\User format.
    Leave empty to use ApplicationPoolIdentity (built-in virtual account).

.PARAMETER AppPoolPassword
    Password for AppPoolIdentity service account as a SecureString. Only used when AppPoolIdentity is set.
    Pass via: -AppPoolPassword (Read-Host 'App pool password' -AsSecureString)

.PARAMETER LdapPassword
    LDAP bind password as a SecureString. Stored as an IIS app pool environment variable
    (PasswordChangeOptions__LdapPassword) so it never touches appsettings.Production.json.
    Skipped when UseAutomaticContext is true (domain-joined servers).

.PARAMETER SmtpPassword
    SMTP relay password as a SecureString. Stored as an IIS app pool environment variable
    (SmtpSettings__Password). Leave empty if the relay allows anonymous submission.

.PARAMETER RecaptchaPrivateKey
    reCAPTCHA v3 secret key as a SecureString. Stored as an IIS app pool environment variable
    (ClientSettings__Recaptcha__PrivateKey). Leave empty if reCAPTCHA is disabled.

.PARAMETER Force
    Skip the interactive upgrade confirmation prompt when an existing installation is detected.
    Use this for unattended / CI deployments.

.EXAMPLE
    # Minimal — uses built-in app pool identity, no HTTPS binding wired yet:
    .\Install-PassReset.ps1

.EXAMPLE
    # Full — service account + certificate + secrets as environment variables:
    .\Install-PassReset.ps1 `
        -AppPoolIdentity "CORP\svc-passreset" `
        -AppPoolPassword (Read-Host 'App pool password' -AsSecureString) `
        -CertThumbprint  "A1B2C3D4E5F6..." `
        -LdapPassword    (Read-Host 'LDAP password' -AsSecureString) `
        -SmtpPassword    (Read-Host 'SMTP password' -AsSecureString)
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string] $SiteName        = 'PassReset',
    [string] $AppPoolName     = 'PassResetPool',
    [string] $PhysicalPath    = 'C:\inetpub\PassReset',
    [string] $PublishFolder   = '',
    [int]    $HttpsPort       = 443,
    [int]    $HttpPort        = 80,
    [string] $CertThumbprint  = '',
    [string]       $AppPoolIdentity = '',
    [SecureString] $AppPoolPassword = $null,

    [SecureString] $LdapPassword        = $null,
    [SecureString] $SmtpPassword        = $null,
    [SecureString] $RecaptchaPrivateKey  = $null,

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ─── Helpers ──────────────────────────────────────────────────────────────────

function Write-Step  { param([string]$Msg) Write-Host "`n[>>] $Msg" -ForegroundColor Cyan }
function Write-Ok    { param([string]$Msg) Write-Host "  [OK] $Msg" -ForegroundColor Green }
function Write-Warn  { param([string]$Msg) Write-Host "  [!!] $Msg" -ForegroundColor Yellow }

# WR-01: Track sites we stopped during port-80 conflict resolution so we can
# restart them if the install later aborts. Initialised outside strict-mode
# gates so Restore-StoppedForeignSites can always read it.
$script:StoppedForeignSites = @()

function Restore-StoppedForeignSites {
    if (-not $script:StoppedForeignSites -or $script:StoppedForeignSites.Count -eq 0) { return }
    foreach ($s in $script:StoppedForeignSites) {
        try {
            Start-Website -Name $s -ErrorAction Stop
            Write-Ok "Restarted foreign site '$s' after abort"
        }
        catch {
            Write-Warn "Could not restart '$s' — restart manually via IIS Manager"
        }
    }
    $script:StoppedForeignSites = @()
}

function Abort       { param([string]$Msg) Restore-StoppedForeignSites; Write-Host "`n[ERR] $Msg`n" -ForegroundColor Red; exit 1 }

# ─── 1. Prerequisites ─────────────────────────────────────────────────────────

Write-Step 'Checking prerequisites'

# IIS
if (-not (Get-Service -Name W3SVC -ErrorAction SilentlyContinue)) {
    Abort 'IIS (W3SVC) is not installed. Install the Web Server (IIS) role first.'
}
Write-Ok 'IIS is installed'

# Required IIS features — valid on Windows Server 2019, 2022, and 2025.
# Note: Web-ASPNET45 / Web-Asp-Net45 are .NET Framework 4.x features and are NOT
# required for ASP.NET Core. They do not exist on Server 2019+ and must not be listed.
# The ASP.NET Core Module is installed by the .NET Hosting Bundle (checked below).
$requiredFeatures = @(
    'Web-Server',
    'Web-WebServer',
    'Web-Static-Content',
    'Web-Default-Doc',
    'Web-Http-Errors',
    'Web-Http-Logging',
    'Web-Filtering',
    'Web-Mgmt-Console'
)

$missing = $requiredFeatures | Where-Object {
    (Get-WindowsFeature -Name $_).InstallState -ne 'Installed'
}

if ($missing) {
    Write-Warn 'Missing IIS features detected:'
    $missing | ForEach-Object { Write-Host "    - $_" -ForegroundColor Yellow }
    if (-not $Force) {
        $consent = Read-Host '  Install missing IIS features now via DISM? [Y/N]'
        if ($consent -notmatch '^[Yy]') {
            Write-Host ''
            Write-Host '  To install manually, run as Administrator:' -ForegroundColor Yellow
            foreach ($f in $missing) {
                Write-Host "    dism /online /enable-feature /featurename:$f /all /norestart" -ForegroundColor Yellow
            }
            Write-Host ''
            exit 0
        }
    } else {
        Write-Ok '-Force specified - installing missing IIS features via DISM'
    }
    foreach ($f in $missing) {
        if ($PSCmdlet.ShouldProcess("IIS feature $f", 'Enable via DISM')) {
            $dismExit = (Start-Process -FilePath dism.exe `
                -ArgumentList @('/online','/enable-feature',"/featurename:$f",'/all','/norestart','/quiet') `
                -Wait -PassThru -NoNewWindow).ExitCode
            # 3010 = success, reboot pending (Microsoft DISM convention)
            if ($dismExit -ne 0 -and $dismExit -ne 3010) {
                Abort "DISM failed enabling $f (exit $dismExit). Run: dism /online /get-featureinfo /featurename:$f"
            }
        }
    }
    Write-Ok 'IIS features enabled via DISM'
} else {
    Write-Ok 'All required IIS features present'
}

# .NET 10 Hosting Bundle
$hostingBundle = Get-ItemProperty `
    -Path 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedhost' `
    -ErrorAction SilentlyContinue

if (-not $hostingBundle) {
    Write-Warn '.NET 10 Hosting Bundle is not installed.'
    Write-Host '  Required: ASP.NET Core 10.0 Runtime (Hosting Bundle)' -ForegroundColor Yellow
    Write-Host '  Download: https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
    Write-Host '  Choose "ASP.NET Core Runtime - Hosting Bundle" for Windows.' -ForegroundColor Yellow
    Write-Host ''
    Write-Host '  Re-run this installer after the Hosting Bundle is installed.' -ForegroundColor Yellow
    Write-Host ''
    exit 0
}

$installedRuntime = $hostingBundle.Version
if (-not ($installedRuntime -match '^10\.')) {
    Write-Warn ".NET 10 Hosting Bundle is required but found version $installedRuntime."
    Write-Host '  Required: ASP.NET Core 10.0 Runtime (Hosting Bundle)' -ForegroundColor Yellow
    Write-Host '  Download: https://dotnet.microsoft.com/download/dotnet/10.0' -ForegroundColor Yellow
    Write-Host ''
    exit 0
}
Write-Ok ".NET Hosting Bundle $installedRuntime detected"

# ─── 2. Resolve publish folder ────────────────────────────────────────────────

Write-Step 'Resolving publish output'

if (-not $PublishFolder) {
    $scriptDir     = Split-Path -Parent $MyInvocation.MyCommand.Path
    $PublishFolder = Join-Path $scriptDir 'publish'
}

if (-not (Test-Path $PublishFolder)) {
    Abort "Publish folder not found: $PublishFolder`nRun: dotnet publish src\PassReset.Web -c Release -o deploy\publish"
}

$webExe = Join-Path $PublishFolder 'PassReset.Web.exe'
if (-not (Test-Path $webExe)) {
    Abort "PassReset.Web.exe not found in $PublishFolder. Ensure you ran dotnet publish first."
}
Write-Ok "Publish folder: $PublishFolder"

# ─── 3. Create deployment folder ──────────────────────────────────────────────

Write-Step "Deploying to $PhysicalPath"

if (-not (Test-Path $PhysicalPath)) {
    New-Item -ItemType Directory -Path $PhysicalPath -Force | Out-Null
    Write-Ok "Created $PhysicalPath"
}

# BRAND DIR — upgrade-safe, never remove on upgrade per FEAT-001 / D-Branding.
# Owned by the operator: contains logo, favicon, and other assets served via /brand/*.
$brandPath = Join-Path $env:ProgramData 'PassReset\brand'
if (-not (Test-Path $brandPath)) {
    New-Item -ItemType Directory -Path $brandPath -Force | Out-Null
    Write-Ok "Created brand directory $brandPath"
} else {
    Write-Ok "Preserving existing brand directory $brandPath"
}

# Stop the site/pool before copying so locked files are released
Import-Module WebAdministration -ErrorAction SilentlyContinue

$poolExists = Test-Path "IIS:\AppPools\$AppPoolName"
$siteExists = Test-Path "IIS:\Sites\$SiteName"

# ─── Upgrade detection ────────────────────────────────────────────────────────

# Initialize flags outside the $siteExists gate so Set-StrictMode does not fault
# on fresh-install paths that never enter the block.
$isDowngrade   = $false
$isReconfigure = $false

if ($siteExists) {
    $deployedExe     = Join-Path $PhysicalPath 'PassReset.Web.exe'
    $currentVersion  = if (Test-Path $deployedExe) {
                           (Get-Item $deployedExe).VersionInfo.FileVersion -replace '\.0$'
                       } else { 'unknown' }
    $incomingVersion = (Get-Item $webExe).VersionInfo.FileVersion -replace '\.0$'

    # Detect downgrade, reconfigure (same version), or upgrade via semantic version comparison.
    # (Flags already initialized above to false so strict-mode on fresh installs is happy.)
    $parsedCurrent  = $null
    $parsedIncoming = $null
    if ([version]::TryParse($currentVersion, [ref]$parsedCurrent) -and
        [version]::TryParse($incomingVersion, [ref]$parsedIncoming)) {
        if     ($parsedIncoming -lt $parsedCurrent) { $isDowngrade   = $true }
        elseif ($parsedIncoming -eq $parsedCurrent) { $isReconfigure = $true }
    }

    Write-Host ''
    Write-Host '  [!!] Existing PassReset installation detected.' -ForegroundColor Yellow
    Write-Host "       Installed : v$currentVersion"              -ForegroundColor Yellow
    Write-Host "       Incoming  : v$incomingVersion"             -ForegroundColor Yellow
    if ($isDowngrade) {
        Write-Host '       WARNING   : Incoming version is OLDER than installed (downgrade).' -ForegroundColor Red
        Write-Host '                   Config schema or data migrations may not reverse cleanly.' -ForegroundColor Red
    } elseif ($isReconfigure) {
        Write-Host '       NOTE      : Incoming version is the SAME as installed — this will RE-CONFIGURE, not upgrade.' -ForegroundColor Yellow
        Write-Host '                   File mirror will be skipped; app-pool / binding / config logic still re-runs.' -ForegroundColor Yellow
    }
    Write-Host ''

    if (-not $Force) {
        $prompt = if ($isDowngrade) {
            '  Continue with DOWNGRADE? [Y/N]'
        } elseif ($isReconfigure) {
            '  Re-configure existing installation? [Y/N]'
        } else {
            '  Continue with upgrade? [Y/N]'
        }
        $confirm = Read-Host $prompt
        if ($confirm -notmatch '^[Yy]') {
            Write-Host "`n  Cancelled." -ForegroundColor Yellow
            exit 0
        }
    } else {
        if ($isDowngrade) {
            Write-Warn '-Force specified - proceeding with downgrade despite version regression'
        } elseif ($isReconfigure) {
            Write-Ok '-Force specified - re-configuring without file mirror'
        } else {
            Write-Ok '-Force specified - skipping upgrade confirmation'
        }
    }
}

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

# Back up the current deployment before overwriting (upgrade only)
$backupPath = $null
if ($siteExists) {
    $backupPath = "${PhysicalPath}_backup_$(Get-Date -Format 'yyyyMMdd-HHmm')"
    Write-Step "Backing up current installation to $backupPath"
    Copy-Item -Path $PhysicalPath -Destination $backupPath -Recurse -Force
    Write-Ok "Backup created: $backupPath"

    # Retention: keep the 3 most recent backups, prune older ones to prevent disk fill.
    $parentDir     = Split-Path -Parent $PhysicalPath
    $leafName      = Split-Path -Leaf   $PhysicalPath
    $backupPattern = "${leafName}_backup_*"
    $oldBackups = Get-ChildItem -Path $parentDir -Directory -Filter $backupPattern -ErrorAction SilentlyContinue |
                  Sort-Object Name -Descending |
                  Select-Object -Skip 3
    foreach ($old in $oldBackups) {
        try {
            Remove-Item -Path $old.FullName -Recurse -Force -ErrorAction Stop
            Write-Ok "Pruned old backup: $($old.Name)"
        } catch {
            Write-Warn "Could not prune old backup $($old.Name): $($_.Exception.Message)"
        }
    }
}

# Copy publish output (robocopy: /MIR = mirror, /NFL /NDL = quiet).
# /XF preserves the operator's production config and any local overrides across mirror.
# /XD preserves the logs folder if ever colocated under the deploy root.
# STAB-002: reconfigure mode (incoming version == installed) skips the mirror so
# the operator's existing publish folder is preserved; app-pool / binding / config
# logic below still re-runs.
if (-not $isReconfigure) {
    robocopy $PublishFolder $PhysicalPath /MIR /NFL /NDL /NJH /NJS /R:3 /W:5 `
        /XF 'appsettings.Production.json' 'appsettings.Local.json' `
        /XD 'logs' | Out-Null
    if ($LASTEXITCODE -ge 8) {
        Abort "robocopy failed with exit code $LASTEXITCODE"
    }
    Write-Ok 'Files copied (preserved: appsettings.Production.json, appsettings.Local.json, logs\)'
} else {
    Write-Ok 'Reconfigure mode - skipping file mirror; existing publish folder preserved'
}

# ─── 4. App pool ──────────────────────────────────────────────────────────────

Write-Step "Configuring app pool: $AppPoolName"

# BUG-003: Capture existing AppPool identity BEFORE any provisioning so we can preserve it on upgrade.
# Initialized to $null so Set-StrictMode does not fault on unset references in the branches below.
$existingIdentityType = $null
$existingIdentity     = $null
if ($poolExists) {
    try {
        # STAB-003: Get-WebConfigurationProperty is reliable across Windows PowerShell 5.1
        # and PowerShell 7.x; the previous Get-ItemProperty | .Value pattern intermittently
        # returned $null on PS 7.x and triggered a spurious "Could not read" warning.
        $appPoolFilter = "system.applicationHost/applicationPools/add[@name='$AppPoolName']"
        $existingIdentityType = (Get-WebConfigurationProperty -PSPath 'IIS:\' `
            -Filter $appPoolFilter -Name processModel.identityType -ErrorAction Stop).Value
        if ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3) {
            $existingIdentity = (Get-WebConfigurationProperty -PSPath 'IIS:\' `
                -Filter $appPoolFilter -Name processModel.userName -ErrorAction Stop).Value
        }
    } catch {
        Write-Warning "Could not read existing AppPool identity: $($_.Exception.Message). Will fall through to default handling."
    }
}

if (-not $poolExists) {
    New-WebAppPool -Name $AppPoolName | Out-Null
    Write-Ok "Created app pool $AppPoolName"
}

# No managed code — ASP.NET Core runs in-process via the hosting module
Set-ItemProperty "IIS:\AppPools\$AppPoolName" managedRuntimeVersion ''
Set-ItemProperty "IIS:\AppPools\$AppPoolName" enable32BitAppOnWin64 $false
Set-ItemProperty "IIS:\AppPools\$AppPoolName" startMode 'AlwaysRunning'
Set-ItemProperty "IIS:\AppPools\$AppPoolName" autoStart $true

# BUG-003: Four-branch identity resolution.
# NEVER read or round-trip processModel.password — it is write-only.
if ($AppPoolIdentity) {
    # Explicit operator override — current behaviour preserved.
    if (-not $AppPoolPassword) {
        Abort 'AppPoolPassword must be supplied when AppPoolIdentity is set. Use: -AppPoolPassword (Read-Host ''App pool password'' -AsSecureString)'
    }
    $bstr          = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($AppPoolPassword)
    $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.userName     $AppPoolIdentity
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.password     $plainPassword
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 3  # SpecificUser
    $plainPassword = $null
    Write-Ok "App pool identity: $AppPoolIdentity (explicit override)"
}
elseif ($poolExists -and ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3)) {
    # Preserve existing service account on upgrade — DO NOT touch identityType, userName, or password.
    Write-Ok "App pool identity preserved: $existingIdentity (use -AppPoolIdentity to override)"
}
elseif ($poolExists) {
    # Existing built-in identity (ApplicationPoolIdentity / NetworkService / LocalService / LocalSystem) — leave untouched on upgrade.
    Write-Ok "App pool identity preserved: $existingIdentityType"
}
else {
    # Fresh install, no override → default to ApplicationPoolIdentity (4).
    Set-ItemProperty "IIS:\AppPools\$AppPoolName" processModel.identityType 4  # ApplicationPoolIdentity
    Write-Ok 'App pool identity: ApplicationPoolIdentity (new pool default)'
}

# ─── 5. IIS site ──────────────────────────────────────────────────────────────

Write-Step "Configuring site: $SiteName"

# STAB-001: detect port-80 conflict before New-Website (fresh install only;
# on upgrade the existing binding is preserved).
$selectedHttpPort = if ($HttpPort -gt 0) { $HttpPort } else { 80 }
if (-not $siteExists -and $selectedHttpPort -eq 80) {
    Write-Step 'Checking port 80 availability'
    $port80Sites = @(Get-WebBinding -Port 80 -Protocol http -ErrorAction SilentlyContinue |
                       Where-Object { $_.ItemXPath -notmatch "name='$SiteName'" })
    if ($port80Sites.Count -gt 0) {
        $conflictSites = $port80Sites | ForEach-Object {
            if ($_.ItemXPath -match "name='([^']+)'") { $matches[1] } else { '<unknown>' }
        } | Sort-Object -Unique
        Write-Warn "Port 80 is already bound by: $($conflictSites -join ', ')"

        if (-not $Force) {
            Write-Host ''
            Write-Host '  Choose how to proceed:' -ForegroundColor Yellow
            Write-Host '    [1] Stop the conflicting site(s) and bind PassReset to port 80' -ForegroundColor Yellow
            Write-Host '    [2] Use an alternate HTTP port (8080-8090, first free)' -ForegroundColor Yellow
            Write-Host '    [3] Abort installation' -ForegroundColor Yellow
            $choice = Read-Host '  Selection [1/2/3]'
            switch ($choice) {
                '1' {
                    foreach ($s in $conflictSites) {
                        if ($PSCmdlet.ShouldProcess("IIS site $s", 'Stop')) {
                            Stop-Website -Name $s -ErrorAction Stop
                            $script:StoppedForeignSites += $s
                            Write-Ok "Stopped site '$s'"
                        }
                    }
                    $selectedHttpPort = 80
                }
                '2' {
                    $selectedHttpPort = $null
                    foreach ($p in 8080..8090) {
                        if (-not (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)) {
                            $selectedHttpPort = $p; break
                        }
                    }
                    if (-not $selectedHttpPort) {
                        Abort 'Ports 80 and 8080-8090 are all in use. Free a port and re-run.'
                    }
                    Write-Ok "Using alternate HTTP port $selectedHttpPort"
                }
                default {
                    Write-Host "`n  Cancelled." -ForegroundColor Yellow; exit 0
                }
            }
        } else {
            # -Force: never silently stop another site (D-02). Pick alternate port.
            $selectedHttpPort = $null
            foreach ($p in 8080..8090) {
                if (-not (Get-NetTCPConnection -LocalPort $p -State Listen -ErrorAction SilentlyContinue)) {
                    $selectedHttpPort = $p; break
                }
            }
            if (-not $selectedHttpPort) {
                Abort '-Force: ports 80 and 8080-8090 all in use; aborting rather than stopping a foreign site.'
            }
            Write-Ok "-Force specified - port 80 in use, defaulting to alternate port $selectedHttpPort"
        }
    } else {
        Write-Ok 'Port 80 is free'
    }
}

if (-not $siteExists) {
    New-Website `
        -Name         $SiteName `
        -PhysicalPath $PhysicalPath `
        -ApplicationPool $AppPoolName `
        -Port         $selectedHttpPort `
        -Force | Out-Null
    Write-Ok "Created site $SiteName (HTTP :$selectedHttpPort placeholder)"
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
        # Remove existing HTTPS binding on this port if any (pipe the object to avoid binding-string mismatch)
        $existingBinding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -ErrorAction SilentlyContinue
        if ($existingBinding) { $existingBinding | Remove-WebBinding }

        New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -SslFlags 0
        $binding = Get-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort
        $binding.AddSslCertificate($CertThumbprint, 'My')
        Write-Ok "HTTPS binding configured on port $HttpsPort"
    }
} else {
    Write-Warn 'No certificate thumbprint supplied — HTTPS binding not configured. Add it manually or re-run with -CertThumbprint.'
}

# HTTP binding — keep for HTTP→HTTPS redirect unless operator explicitly passes -HttpPort 0
if ($CertThumbprint -and $HttpPort -le 0) {
    $httpBinding = Get-WebBinding -Name $SiteName -Protocol http -ErrorAction SilentlyContinue
    if ($httpBinding) {
        $httpBinding | Remove-WebBinding
        Write-Ok 'Removed HTTP binding (HTTPS-only mode: -HttpPort 0)'
    }
} elseif ($CertThumbprint) {
    # Ensure the HTTP binding exists on the configured port so ASP.NET Core
    # UseHttpsRedirection() can receive and redirect plain-HTTP requests.
    $httpBinding = Get-WebBinding -Name $SiteName -Protocol http -Port $HttpPort -ErrorAction SilentlyContinue
    if (-not $httpBinding) {
        New-WebBinding -Name $SiteName -Protocol http -Port $HttpPort
        Write-Ok "HTTP :$HttpPort binding retained for HTTP→HTTPS redirect"
    } else {
        Write-Ok "HTTP :$HttpPort binding present (HTTP→HTTPS redirect active)"
    }
}

# STAB-001 D-03: announce the reachable URLs so operators don't have to inspect IIS.
$hostHeader = $env:COMPUTERNAME
if (-not $siteExists) {
    Write-Ok "PassReset reachable at http://${hostHeader}:${selectedHttpPort}/"
} else {
    # WR-02: read the actual HTTP binding(s) from IIS — previous installs on
    # alternate ports (e.g. 8081) must not be mis-announced as :$HttpPort.
    $httpBindings = @(Get-WebBinding -Name $SiteName -Protocol http -ErrorAction SilentlyContinue)
    if ($httpBindings.Count -gt 0) {
        foreach ($b in $httpBindings) {
            # bindingInformation is "*:port:host"
            $port = ($b.bindingInformation -split ':')[1]
            Write-Ok "PassReset reachable at http://${hostHeader}:${port}/ (HTTP binding retained from previous install)"
        }
    } else {
        Write-Ok 'PassReset upgrade complete — no HTTP binding present (HTTPS-only mode)'
    }
}
if ($CertThumbprint) {
    Write-Ok "PassReset reachable at https://${hostHeader}:${HttpsPort}/ (HTTPS binding configured)"
}

# ─── 6. NTFS permissions ──────────────────────────────────────────────────────

Write-Step 'Setting NTFS permissions'

# BUG-003: Resolve the *actual* runtime identity so the ACE matches the principal the worker runs as —
# including the preserved-on-upgrade case where $existingIdentity holds the operator's service account.
$identity = if ($AppPoolIdentity) {
    $AppPoolIdentity
} elseif ($existingIdentityType -eq 'SpecificUser' -or $existingIdentityType -eq 3) {
    $existingIdentity
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

# Logs folder — follows IIS convention (%SystemDrive%\inetpub\logs\PassReset),
# kept outside wwwroot so logs are never web-accessible. Serilog writes
# passreset-YYYYMMDD.log here (see appsettings.json → Serilog.WriteTo.File.path).
$logsPath = Join-Path $env:SystemDrive 'inetpub\logs\PassReset'
if (-not (Test-Path $logsPath)) { New-Item -ItemType Directory -Path $logsPath -Force | Out-Null }

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
    # The template is copied into publish output by Publish-PassReset.ps1
    $templateFile = Join-Path $PhysicalPath 'appsettings.Production.template.json'

    if (Test-Path $templateFile) {
        Copy-Item $templateFile $prodConfig
        Write-Ok "Written to $prodConfig — fill in your domain details before starting the site."
    } else {
        Write-Warn 'appsettings.Production.template.json not found — create appsettings.Production.json manually.'
    }
}

# ─── 8. App pool environment variables (secrets) ─────────────────────────
# Secrets are stored as IIS app pool environment variables so they never
# touch appsettings.Production.json. Existing values are preserved.

function Set-PoolEnvVar {
    param([string] $PoolName, [string] $VarName, [SecureString] $SecureValue)

    # Check if the variable already exists on the pool
    $existing = Get-WebConfigurationProperty `
        -PSPath 'MACHINE/WEBROOT/APPHOST' `
        -Filter "system.applicationHost/applicationPools/add[@name='$PoolName']/environmentVariables" `
        -Name Collection `
        -ErrorAction SilentlyContinue |
        Where-Object { $_.name -eq $VarName }

    if ($existing) {
        Write-Warn "$VarName already set on $PoolName — not overwriting"
        return
    }

    $bstr  = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    $plain = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($bstr)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

    Add-WebConfigurationProperty `
        -PSPath 'MACHINE/WEBROOT/APPHOST' `
        -Filter "system.applicationHost/applicationPools/add[@name='$PoolName']/environmentVariables" `
        -Name '.' `
        -Value @{ name = $VarName; value = $plain }

    $plain = $null
    Write-Ok "$VarName → $PoolName environment"
}

$secretsSet = $false

if ($LdapPassword) {
    Write-Step 'Configuring app pool environment variables (secrets)'
    Set-PoolEnvVar $AppPoolName 'PasswordChangeOptions__LdapPassword' $LdapPassword
    $secretsSet = $true
}

if ($SmtpPassword) {
    if (-not $secretsSet) { Write-Step 'Configuring app pool environment variables (secrets)' }
    Set-PoolEnvVar $AppPoolName 'SmtpSettings__Password' $SmtpPassword
    $secretsSet = $true
}

if ($RecaptchaPrivateKey) {
    if (-not $secretsSet) { Write-Step 'Configuring app pool environment variables (secrets)' }
    Set-PoolEnvVar $AppPoolName 'ClientSettings__Recaptcha__PrivateKey' $RecaptchaPrivateKey
    $secretsSet = $true
}

# ─── 9. Start site ────────────────────────────────────────────────────────────

Write-Step 'Starting app pool and site'

try {
    Start-WebAppPool -Name $AppPoolName -ErrorAction Stop
    Start-Website    -Name $SiteName    -ErrorAction Stop

    # Give the worker process a moment to actually start (or crash).
    Start-Sleep -Seconds 3
    $poolStateAfter = (Get-WebAppPoolState -Name $AppPoolName).Value
    $siteStateAfter = (Get-WebsiteState    -Name $SiteName).Value
    if ($poolStateAfter -ne 'Started' -or $siteStateAfter -ne 'Started') {
        throw "Pool state: $poolStateAfter, Site state: $siteStateAfter — expected Started"
    }

    Write-Ok "App pool $AppPoolName started"
    Write-Ok "Site $SiteName started"
}
catch {
    Write-Host ''
    Write-Host "[ERR] Startup failed: $($_.Exception.Message)" -ForegroundColor Red

    if ($backupPath -and (Test-Path $backupPath)) {
        Write-Warn 'Attempting automatic rollback from backup...'
        try {
            if ((Get-WebAppPoolState -Name $AppPoolName).Value -eq 'Started') {
                Stop-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
            }
            if ((Get-WebsiteState -Name $SiteName).Value -eq 'Started') {
                Stop-Website -Name $SiteName -ErrorAction SilentlyContinue
            }
            robocopy $backupPath $PhysicalPath /MIR /NFL /NDL /NJH /NJS /R:3 /W:5 | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy rollback failed ($LASTEXITCODE)" }
            Start-WebAppPool -Name $AppPoolName -ErrorAction Stop
            Start-Website    -Name $SiteName    -ErrorAction Stop
            Write-Ok "Rolled back to backup: $backupPath"
            Abort 'Upgrade failed — previous version has been restored. Investigate the new build before retrying.'
        }
        catch {
            Abort "Rollback FAILED: $($_.Exception.Message)`nManual recovery: stop site, robocopy '$backupPath' → '$PhysicalPath', start site."
        }
    } else {
        Abort 'Startup failed and no backup is available (fresh install). Check Event Viewer → Application log for ASP.NET Core errors.'
    }
}

# ─── 9b. Config schema drift detection ────────────────────────────────────────
# After a successful upgrade, compare keys in the incoming template against the
# live production config. New keys from the template are reported so operators
# know what to add — no auto-merge (risky with nested/array values).

if ($siteExists -and (Test-Path $prodConfig)) {
    $templateFile = Join-Path $PhysicalPath 'appsettings.Production.template.json'
    if (Test-Path $templateFile) {
        try {
            $templateJson = Get-Content $templateFile -Raw | ConvertFrom-Json
            $liveJson     = Get-Content $prodConfig   -Raw | ConvertFrom-Json

            function Get-JsonKeyPaths {
                param($Node, [string]$Prefix = '')
                $paths = @()
                if ($null -ne $Node -and $Node -is [PSCustomObject]) {
                    foreach ($prop in $Node.PSObject.Properties) {
                        $path = if ($Prefix) { "$Prefix.$($prop.Name)" } else { $prop.Name }
                        $paths += $path
                        if ($prop.Value -is [PSCustomObject]) {
                            $paths += Get-JsonKeyPaths -Node $prop.Value -Prefix $path
                        }
                    }
                }
                return $paths
            }

            $templateKeys = Get-JsonKeyPaths -Node $templateJson
            $liveKeys     = Get-JsonKeyPaths -Node $liveJson
            $newKeys      = $templateKeys | Where-Object { $liveKeys -notcontains $_ }

            if ($newKeys) {
                Write-Host ''
                Write-Warn 'Config schema drift detected — new keys in template not present in live config:'
                foreach ($k in $newKeys) { Write-Host "    + $k" -ForegroundColor Yellow }
                Write-Host '    Review appsettings.Production.template.json and add any required keys to' -ForegroundColor Yellow
                Write-Host "    $prodConfig manually." -ForegroundColor Yellow
            }
        } catch {
            Write-Warn "Config schema drift check skipped: $($_.Exception.Message)"
        }
    }
}

# ─── Done ─────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '======================================================' -ForegroundColor Cyan
if ($backupPath) {
    Write-Host '  PassReset upgraded successfully.' -ForegroundColor Green
    Write-Host ''
    Write-Host '  Backup of previous installation:' -ForegroundColor Yellow
    Write-Host "    $backupPath"                    -ForegroundColor Yellow
    Write-Host '  To roll back manually: stop the site, robocopy the backup'
    Write-Host '  folder back to $PhysicalPath, then start the site.'
} else {
    Write-Host '  PassReset installed successfully.' -ForegroundColor Green
}
Write-Host ''
Write-Host '  Next steps:' -ForegroundColor Yellow
Write-Host "  1. Edit $prodConfig"
Write-Host '     - Set DefaultDomain, SmtpSettings, etc.'
if (-not $CertThumbprint) {
Write-Host '  2. Add an HTTPS certificate binding in IIS Manager.'
}
if (-not $secretsSet) {
Write-Host '  2. Set secrets via environment variables (recommended) or in appsettings.Production.json.'
Write-Host '     Re-run with -LdapPassword / -SmtpPassword / -RecaptchaPrivateKey, or set manually.'
Write-Host '     See docs/Secret-Management.md for details.'
}
Write-Host '  3. Browse to the site and test with UseDebugProvider: true first.'
Write-Host '  4. Set UseDebugProvider: false when ready for production.'
Write-Host '======================================================' -ForegroundColor Cyan
Write-Host ''
