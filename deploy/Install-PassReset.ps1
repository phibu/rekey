#Requires -RunAsAdministrator
#Requires -Version 7.0
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

    [ValidateSet('Merge','Review','None')]
    [string] $ConfigSync = '',   # empty -> resolved post-upgrade-detection: prompt if interactive, 'Merge' if -Force, 'None' if fresh install

    [switch] $Force,

    # STAB-019: bypass post-deploy /api/health + /api/password verification (air-gapped hosts only).
    # Default $false — verification runs by default, including under -Force (D-06/D-07).
    [switch] $SkipHealthCheck = $false
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

# ─── Config Sync Helpers (plan 08-05 / STAB-010) ──────────────────────────────
# Schema-driven additive merge: walks appsettings.schema.json (NOT the template),
# enumerates every leaf key + default, and adds anything missing from the operator's
# live appsettings.Production.json. Never modifies existing values (D-13). Arrays
# are atomic (D-14). Obsolete keys (x-passreset-obsolete) are reported in Merge
# mode and prompted in Review mode. Key-path separator: ':' (ASP.NET Core IOptions
# convention, matches env-var PasswordChangeOptions__LdapPort notation).

function Get-SchemaKeyManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Schema,
        [string] $Prefix = ''
    )
    $entries = @()
    if ($null -eq $Schema -or $null -eq $Schema.properties) { return $entries }
    foreach ($prop in $Schema.properties.PSObject.Properties) {
        $name = $prop.Name
        $node = $prop.Value
        $path = if ($Prefix) { "${Prefix}:${name}" } else { $name }
        $isObj = ($node.type -eq 'object') -or ($null -ne $node.properties)
        if ($isObj) {
            # Recurse into nested object (don't emit a leaf for the object itself)
            $entries += Get-SchemaKeyManifest -Schema $node -Prefix $path
        } else {
            # Leaf: scalar OR array (arrays atomic per D-14)
            # Gate property access behind PSObject.Properties.Name -contains checks so that
            # Set-StrictMode -Version Latest (used by the installer) does not throw when a
            # schema node omits x-passreset-obsolete / x-passreset-obsolete-since / default.
            $isObsolete = $false
            if ($node.PSObject.Properties.Name -contains 'x-passreset-obsolete') {
                $isObsolete = ($node.'x-passreset-obsolete' -eq $true)
            }
            $obsoleteSince = $null
            if ($node.PSObject.Properties.Name -contains 'x-passreset-obsolete-since') {
                $obsoleteSince = $node.'x-passreset-obsolete-since'
            }
            $defaultValue = $null
            $hasDefault = $node.PSObject.Properties.Name -contains 'default'
            if ($hasDefault) {
                $defaultValue = $node.default
            }
            $entries += [PSCustomObject]@{
                Path           = $path
                Default        = $defaultValue
                HasDefault     = $hasDefault
                IsObsolete     = $isObsolete
                ObsoleteSince  = $obsoleteSince
                Type           = $node.type
            }
        }
    }
    return $entries
}

function Get-LiveValueAtPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Config,
        [Parameter(Mandatory)] [string] $Path
    )
    $segments = $Path -split ':'
    $node = $Config
    foreach ($seg in $segments) {
        if ($null -eq $node -or -not ($node -is [PSCustomObject])) {
            return @{ Exists = $false; Value = $null }
        }
        $prop = $node.PSObject.Properties[$seg]
        if ($null -eq $prop) {
            return @{ Exists = $false; Value = $null }
        }
        $node = $prop.Value
    }
    return @{ Exists = $true; Value = $node }
}

function Set-LiveValueAtPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Config,
        [Parameter(Mandatory)] [string] $Path,
        $Value
    )
    $segments = $Path -split ':'
    $node = $Config
    for ($i = 0; $i -lt $segments.Length - 1; $i++) {
        $seg = $segments[$i]
        $prop = $node.PSObject.Properties[$seg]
        if ($null -eq $prop) {
            # Create intermediate object
            $node | Add-Member -NotePropertyName $seg -NotePropertyValue ([PSCustomObject]@{})
            $prop = $node.PSObject.Properties[$seg]
        } elseif (-not ($prop.Value -is [PSCustomObject])) {
            # Existing scalar where we expected object; cannot proceed (don't overwrite operator value)
            throw "Cannot create nested key at '$Path' - intermediate '$seg' exists as a non-object value."
        }
        $node = $prop.Value
    }
    $leaf = $segments[-1]
    if ($node.PSObject.Properties.Name -contains $leaf) {
        # Per D-09/D-13: never modify existing values
        return $false
    }
    $node | Add-Member -NotePropertyName $leaf -NotePropertyValue $Value
    return $true
}

function Remove-LiveValueAtPath {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] $Config,
        [Parameter(Mandatory)] [string] $Path
    )
    $segments = $Path -split ':'
    $node = $Config
    for ($i = 0; $i -lt $segments.Length - 1; $i++) {
        $seg = $segments[$i]
        $prop = $node.PSObject.Properties[$seg]
        if ($null -eq $prop -or -not ($prop.Value -is [PSCustomObject])) { return $false }
        $node = $prop.Value
    }
    $leaf = $segments[-1]
    if ($node.PSObject.Properties.Name -notcontains $leaf) { return $false }
    $node.PSObject.Properties.Remove($leaf)
    return $true
}

function Sync-AppSettingsAgainstSchema {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SchemaPath,
        [Parameter(Mandatory)] [string] $ConfigPath,
        [Parameter(Mandatory)] [ValidateSet('Merge','Review','None')] [string] $Mode
    )
    if ($Mode -eq 'None') {
        Write-Ok 'Config sync skipped (-ConfigSync None)'
        return
    }
    if (-not (Test-Path $SchemaPath)) {
        Write-Warn "Schema file not found at $SchemaPath - cannot sync."
        return
    }
    if (-not (Test-Path $ConfigPath)) {
        Write-Warn "Live config not found at $ConfigPath - nothing to sync."
        return
    }

    $schema = Get-Content $SchemaPath -Raw | ConvertFrom-Json
    $live   = Get-Content $ConfigPath -Raw | ConvertFrom-Json

    $manifest = Get-SchemaKeyManifest -Schema $schema
    $additions = @()
    $obsoleteFound = @()
    $modified = $false

    foreach ($entry in $manifest) {
        $look = Get-LiveValueAtPath -Config $live -Path $entry.Path
        if ($entry.IsObsolete) {
            if ($look.Exists) {
                $obsoleteFound += $entry
                if ($Mode -eq 'Review') {
                    $reply = Read-Host "  Remove obsolete key '$($entry.Path)' (no longer used as of v$($entry.ObsoleteSince))? [Y/N]"
                    if ($reply -match '^[Yy]') {
                        if (Remove-LiveValueAtPath -Config $live -Path $entry.Path) {
                            $modified = $true
                            Write-Ok "  - Removed obsolete: $($entry.Path)"
                        }
                    }
                } else {
                    # Merge mode: report only, never remove (D-11 safe default)
                    Write-Warn "Obsolete: $($entry.Path) - no longer used as of v$($entry.ObsoleteSince). Safe to remove."
                }
            }
            continue
        }
        if (-not $look.Exists) {
            if (-not $entry.HasDefault) {
                # No default in schema -> can't auto-add; warn so operator knows.
                Write-Warn "Missing key '$($entry.Path)' has no default in schema; not added (operator must set manually)."
                continue
            }
            if ($Mode -eq 'Review') {
                $defaultDisplay = if ($entry.Default -is [array]) { '[' + (($entry.Default | ForEach-Object { "`"$_`"" }) -join ',') + ']' } else { "$($entry.Default)" }
                $reply = Read-Host "  Add '$($entry.Path)' with default = $defaultDisplay? [Y/N] [Y]"
                if ($reply -and $reply -notmatch '^[Yy]' -and $reply -notmatch '^$') { continue }
            }
            try {
                if (Set-LiveValueAtPath -Config $live -Path $entry.Path -Value $entry.Default) {
                    $modified = $true
                    $additions += $entry
                    Write-Ok "  + $($entry.Path) = $($entry.Default)"
                }
            } catch {
                Write-Warn "Could not add '$($entry.Path)': $($_.Exception.Message)"
            }
        }
    }

    if ($modified) {
        $live | ConvertTo-Json -Depth 32 | Set-Content -Path $ConfigPath -Encoding UTF8 -NoNewline
        Write-Ok "Wrote $($additions.Count) addition(s) to $ConfigPath"
    } else {
        Write-Ok 'Config is in sync with schema; no changes written.'
    }

    if ($obsoleteFound.Count -gt 0 -and $Mode -eq 'Merge') {
        Write-Warn "$($obsoleteFound.Count) obsolete key(s) reported above. Re-run with -ConfigSync Review to remove interactively."
    }
}

# ─── Schema Drift Check (plan 08-06 / STAB-012) ───────────────────────────────
# Purely diagnostic check run AFTER sync. Reads the schema (D-17) as the
# authoritative source for required keys and ALWAYS runs on upgrade (D-18)
# rather than silently skipping when the live config happens to parse. Never
# mutates the file - reports missing required keys, obsolete keys still
# present, and (informationally) unknown top-level keys. Reuses the
# Get-SchemaKeyManifest / Get-LiveValueAtPath helpers from plan 08-05.

function Test-AppSettingsSchemaDrift {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $SchemaPath,
        [Parameter(Mandatory)] [string] $ConfigPath
    )
    if (-not (Test-Path $SchemaPath)) {
        Write-Warn "Schema not found at $SchemaPath - drift check skipped."
        return [PSCustomObject]@{ Missing = @(); Obsolete = @(); Unknown = @(); Skipped = $true }
    }
    if (-not (Test-Path $ConfigPath)) {
        Write-Warn "Live config not found at $ConfigPath - drift check skipped."
        return [PSCustomObject]@{ Missing = @(); Obsolete = @(); Unknown = @(); Skipped = $true }
    }

    # Intentionally NO try/catch around parsing: D-18 forbids silently skipping
    # when live config parses OK (the old bug). If JSON parsing fails here, the
    # 08-04 pre-flight should have caught it first; if it didn't, surface the
    # exception so the operator sees the problem.
    $schema = Get-Content $SchemaPath -Raw | ConvertFrom-Json
    $live   = Get-Content $ConfigPath -Raw | ConvertFrom-Json

    $manifest = Get-SchemaKeyManifest -Schema $schema

    $missing  = @()
    $obsolete = @()
    foreach ($entry in $manifest) {
        $look = Get-LiveValueAtPath -Config $live -Path $entry.Path
        if ($entry.IsObsolete) {
            if ($look.Exists) { $obsolete += $entry }
        } elseif (-not $look.Exists) {
            $missing += $entry
        }
    }

    # Unknown top-level keys (informational only - schema allows additionalProperties: true)
    $schemaTopKeys = @()
    if ($schema.properties) {
        $schemaTopKeys = @($schema.properties.PSObject.Properties.Name)
    }
    $liveTopKeys = @($live.PSObject.Properties.Name)
    $unknown = @($liveTopKeys | Where-Object { $schemaTopKeys -notcontains $_ })

    return [PSCustomObject]@{
        Missing  = $missing
        Obsolete = $obsolete
        Unknown  = $unknown
        Skipped  = $false
    }
}

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

# Windows Event Log source (D-07 runtime half from plan 08-03).
# Registered by the installer once on fresh install so that the ASP.NET Core host's
# EventLog.WriteEntry("PassReset", ...) calls at startup actually surface in Event Viewer.
Write-Step 'Ensuring Windows Event Log source PassReset is registered'
try {
    if (-not [System.Diagnostics.EventLog]::SourceExists('PassReset')) {
        New-EventLog -LogName Application -Source PassReset -ErrorAction Stop
        Write-Ok "Registered Event Log source 'PassReset' under 'Application' log"
    } else {
        Write-Ok "Event Log source 'PassReset' already registered"
    }
} catch {
    Write-Warn "Could not register Event Log source 'PassReset': $($_.Exception.Message)"
    Write-Warn 'Startup validation failures will not appear in Event Viewer until source is registered.'
    # Do NOT Abort — install can proceed; runtime EventLog.WriteEntry will silently swallow.
}

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

# Stop the site/pool before copying so locked files are released.
# Fail loudly if the WebAdministration module can't load — otherwise downstream
# uses of the IIS:\ drive fault with a cryptic "drive not found" error (the
# exception formatter renders the missing drive name as "ISS", which looks like
# a typo but is actually Windows truncation of the word "IIS:" in its display).
try {
    # WebAdministration is a legacy PSSnapIn-based module that only exists in
    # Windows PowerShell (Desktop edition). PS 7 can't load it natively
    # (PSSnapIn types were removed from PS Core) — importing always goes
    # through the WinPSCompat remoting session. Silence the noisy
    # deserialization warning emitted on import.
    Import-Module WebAdministration -WarningAction SilentlyContinue -ErrorAction Stop
} catch {
    Abort @"
Failed to load the WebAdministration PowerShell module — the IIS:\ drive
won't be available so this installer cannot configure AppPools or Sites.

Cause: IIS Management Scripts and Tools role is not installed on this host.

Fix: run as Administrator and enable the feature, then re-run this installer:
  dism.exe /online /enable-feature /featurename:IIS-ManagementScriptingTools /all

Underlying error: $($_.Exception.Message)
"@
}

# Importing WebAdministration via the WinPSCompat session registers the IIS:\
# PSDrive INSIDE the compat session, not in our local PS 7 session. Downstream
# Set-ItemProperty "IIS:\AppPools\..." calls then fail with the notorious
# 'Cannot find drive. A drive with the name "IIS" does not exist' error.
# Use the modern IISAdministration module (ships with IIS 8.5+, PS Core-native)
# for drive-less operations, OR register the IIS:\ drive manually using the
# WebAdministration provider surfaced through the compat session.
#
# Explicit drive registration is the minimal change — preserves all existing
# Set-ItemProperty "IIS:\..." call sites below.
if (-not (Get-PSDrive -Name 'IIS' -ErrorAction SilentlyContinue)) {
    try {
        New-PSDrive -Name 'IIS' -PSProvider WebAdministration -Root 'MACHINE/WEBROOT/APPHOST' `
            -Scope Script -ErrorAction Stop | Out-Null
    } catch {
        Abort @"
The WebAdministration module loaded, but the IIS:\ PSDrive is not available
in this PowerShell session — so downstream Set-ItemProperty "IIS:\..." calls
will fail with 'Cannot find drive'.

This happens on PS 7 because WebAdministration loads through the WinPSCompat
remoting session, which registers the IIS:\ drive inside the compat process,
not locally. The automatic fallback (New-PSDrive with the WebAdministration
provider) also failed.

Underlying error: $($_.Exception.Message)

This installer requires PowerShell 7. To help us diagnose and fix the
compat-session issue on your host, run the read-only diagnostic probe
and attach the output to an issue:

  pwsh -NoProfile -File .\Test-PS7Iis.ps1
  # Then file at https://github.com/phibu/AD-Passreset-Portal/issues
  # with your PSVersionTable, OS build, and the probe output.
"@
    }
}

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

# ─── Pre-flight: validate live appsettings.Production.json against schema (D-05) ───
# Runs on upgrade ONLY (fresh installs have no live config yet). Halts install
# before any sync work in plan 08-05 so invalid configs never get auto-merged.
# STAB-009 install-time half; the runtime half is enforced at startup (plan 08-03).
$prodConfig = Join-Path $PhysicalPath 'appsettings.Production.json'

if ($siteExists -and (Test-Path $prodConfig)) {
    Write-Step 'Validating appsettings.Production.json against schema (pre-flight)'

    $schemaFile = Join-Path $PhysicalPath 'appsettings.schema.json'
    if (-not (Test-Path $schemaFile)) {
        Write-Warn "appsettings.schema.json not found at $schemaFile - skipping pre-flight validation."
        Write-Warn 'This release was built without the schema. Pre-flight will not catch invalid config.'
    } else {
        $validationErrors = @()
        try {
            $valid = Test-Json `
                -Path $prodConfig `
                -SchemaFile $schemaFile `
                -ErrorVariable validationErrors `
                -ErrorAction SilentlyContinue
        } catch {
            $valid = $false
            $validationErrors = @($_.Exception.Message)
        }
        if (-not $valid) {
            $errorDetail = ($validationErrors | ForEach-Object { "    $_" }) -join "`n"
            Abort "appsettings.Production.json failed schema validation:`n$errorDetail`n  Edit $prodConfig and re-run Install-PassReset.ps1."
        }
        Write-Ok 'appsettings.Production.json conforms to schema'
    }
}

# ─── Resolve config sync mode (STAB-011 / D-12, D-13) ──────────────────────────
# Runs AFTER robocopy (so template is present on upgrade path) and BEFORE any
# sync work. The resolved $ConfigSync value drives the additive-merge sync in
# plan 08-05 and the drift-check rewrite in plan 08-06.

Write-Step 'Resolving config sync mode'
if (-not $ConfigSync) {
    if ($Force) {
        $ConfigSync = 'Merge'
        Write-Ok "-Force specified - defaulting to -ConfigSync Merge"
    } elseif ($siteExists) {
        # Upgrade detected, interactive session — prompt per D-13.
        $reply = Read-Host '  Config sync: [M]erge additions / [R]eview each / [S]kip? [M]'
        $ConfigSync = switch -Regex ($reply) {
            '^[Rr]' { 'Review' }
            '^[Ss]' { 'None' }
            default { 'Merge' }
        }
        Write-Ok "Config sync mode: $ConfigSync"
    } else {
        # Fresh install — template was just copied verbatim; nothing to sync.
        $ConfigSync = 'None'
    }
} else {
    Write-Ok "Config sync mode (from -ConfigSync param): $ConfigSync"
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

# ----- STAB-019: post-deploy verification -----
# Verify the freshly deployed app responds on /api/health + /api/password before
# declaring success. Retries 10x at 2s intervals (~20s worst case, matches AppPool
# cold-start). Hard-fails with exit 1 on final failure. Runs under -Force (D-06/D-07).
# Only -SkipHealthCheck (air-gapped hosts) bypasses (D-10).
if (-not $SkipHealthCheck) {
    $baseUrl = if ($CertThumbprint -and $HttpsPort) {
        "https://${hostHeader}:${HttpsPort}"
    } else {
        "http://${hostHeader}:${selectedHttpPort}"
    }

    $maxAttempts  = 10
    $attempt      = 0
    $ok           = $false
    $lastHealth   = $null
    $lastSettings = $null

    Write-Step "Verifying deployment at $baseUrl (up to $maxAttempts x 2s)"

    do {
        Start-Sleep -Seconds 2
        $attempt++
        try {
            $lastHealth   = Invoke-WebRequest -Uri "$baseUrl/api/health"   -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            $lastSettings = Invoke-WebRequest -Uri "$baseUrl/api/password" -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            if ($lastHealth.StatusCode -eq 200 -and $lastSettings.StatusCode -eq 200) {
                $ok = $true
            }
        } catch {
            Write-Warning ("Attempt {0}/{1}: {2}" -f $attempt, $maxAttempts, $_.Exception.Message)
        }
    } while (-not $ok -and $attempt -lt $maxAttempts)

    if (-not $ok) {
        $bodySnippet = if ($lastHealth) { $lastHealth.Content } else { "(no response)" }
        Write-Error ("Post-deploy health check failed after {0} attempts. Last /api/health response: {1}" -f $maxAttempts, $bodySnippet)
        exit 1
    }

    try {
        $body  = $lastHealth.Content | ConvertFrom-Json
        $ad    = $body.checks.ad.status
        $smtp  = $body.checks.smtp.status
        $expir = $body.checks.expiryService.status
        Write-Ok ("Health OK -- AD: {0}, SMTP: {1}, ExpiryService: {2}" -f $ad, $smtp, $expir)
    } catch {
        Write-Warning ("Health endpoint returned 200 but JSON parse failed: {0}" -f $_.Exception.Message)
        Write-Ok "Health OK (body could not be parsed -- status 200 accepted)"
    }
} else {
    Write-Step "Skipping post-deploy health check (-SkipHealthCheck specified)"
}
# ----- /STAB-019 -----

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

# ─── 9b. Config sync (schema-driven additive merge — plan 08-05 / STAB-010) ───
# Walks appsettings.schema.json, adds any missing keys to the operator's live
# appsettings.Production.json using schema defaults. NEVER modifies existing
# values (D-13). Arrays atomic (D-14). Obsolete keys reported (Merge) or
# prompted (Review). $ConfigSync was resolved earlier in the script (Merge /
# Review / None) based on -ConfigSync param, -Force, or interactive prompt.

if ($siteExists -and (Test-Path $prodConfig)) {
    Write-Step 'Syncing appsettings.Production.json against schema'
    $schemaFile = Join-Path $PhysicalPath 'appsettings.schema.json'
    Sync-AppSettingsAgainstSchema `
        -SchemaPath $schemaFile `
        -ConfigPath $prodConfig `
        -Mode $ConfigSync
}

# ─── 9c. Schema drift check (plan 08-06 / STAB-012) ───────────────────────────
# Runs UNCONDITIONALLY on every upgrade (D-18) - no silent-skip when live
# parses OK. Schema is the source of truth (D-17). Purely diagnostic - any
# mutation is sync's job (9b above). Positioned AFTER sync so the report
# reflects the post-sync state: 'Missing' only surfaces when sync was None or
# the schema had no default for a required key.

if ($siteExists) {
    Write-Step 'Checking appsettings.Production.json for schema drift'
    $drift = Test-AppSettingsSchemaDrift `
        -SchemaPath (Join-Path $PhysicalPath 'appsettings.schema.json') `
        -ConfigPath $prodConfig

    if ($drift.Skipped) {
        # Already warned inside the function - nothing more to do.
    } else {
        $hasDrift = $false
        if ($drift.Missing.Count -gt 0) {
            $hasDrift = $true
            Write-Warn "Schema drift: $($drift.Missing.Count) required key(s) still missing from ${prodConfig}:"
            foreach ($m in $drift.Missing) {
                $defaultHint = if ($m.HasDefault) { " (schema default: $($m.Default))" } else { ' (no default in schema; manual entry required)' }
                Write-Host "    - $($m.Path)$defaultHint" -ForegroundColor Yellow
            }
            if ($ConfigSync -eq 'None') {
                Write-Warn 'Re-run with -ConfigSync Merge to add missing keys automatically.'
            }
        }
        if ($drift.Obsolete.Count -gt 0) {
            $hasDrift = $true
            Write-Warn "Schema drift: $($drift.Obsolete.Count) obsolete key(s) present in ${prodConfig}:"
            foreach ($o in $drift.Obsolete) {
                Write-Host "    - $($o.Path) (obsolete since v$($o.ObsoleteSince))" -ForegroundColor Yellow
            }
            Write-Warn 'Re-run with -ConfigSync Review to remove obsolete keys interactively.'
        }
        if ($drift.Unknown.Count -gt 0) {
            Write-Host "  [i] $($drift.Unknown.Count) unknown top-level key(s) in $prodConfig (allowed; informational only):" -ForegroundColor DarkGray
            foreach ($u in $drift.Unknown) {
                Write-Host "    - $u" -ForegroundColor DarkGray
            }
        }
        if (-not $hasDrift) {
            Write-Ok 'No schema drift detected.'
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
