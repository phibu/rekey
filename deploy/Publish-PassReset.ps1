#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the frontend, publishes the .NET app, and packages a release zip.

.DESCRIPTION
    Run this from the repo root before running Install-PassReset.ps1.
    Output:
      deploy\publish\            — raw publish output (used by Install-PassReset.ps1)
      deploy\PassReset-<ver>.zip — release package ready for distribution

    Release workflow (run on every version bump):
      1. Tag the commit: git tag v1.2.0
      2. Run: .\deploy\Publish-PassReset.ps1
      3. Upload deploy\PassReset-v1.2.0.zip to the GitHub release as an asset.
         The zip includes the published app and Install-PassReset.ps1 so that
         users can deploy without needing to build from source.

.PARAMETER Configuration
    Build configuration. Default: Release

.PARAMETER Version
    Version string embedded in the zip filename.
    Defaults to the latest git tag (e.g. v1.0.0), or 'dev' if no tag exists.
#>
param(
    [string] $Configuration = 'Release',
    [string] $Version       = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot   # one level up from deploy\
$clientApp  = Join-Path $repoRoot 'src\PassReset.Web\ClientApp'
$publishOut = Join-Path $PSScriptRoot 'publish'

# ── Resolve version ───────────────────────────────────────────────────────────
if (-not $Version) {
    $Version = git -C $repoRoot describe --tags --abbrev=0 2>$null
    if (-not $Version) { $Version = 'dev' }
}

# ── Frontend ──────────────────────────────────────────────────────────────────
Write-Host "`n[>>] Building React frontend..." -ForegroundColor Cyan
Push-Location $clientApp
try {
    npm ci --silent
    npm run build
} finally {
    Pop-Location
}
Write-Host "  [OK] Frontend built → src\PassReset.Web\wwwroot\" -ForegroundColor Green

# ── .NET publish ──────────────────────────────────────────────────────────────
Write-Host "`n[>>] Publishing .NET app ($Configuration)..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\PassReset.Web\PassReset.Web.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $publishOut
Write-Host "  [OK] Published to $publishOut" -ForegroundColor Green

# ── Package release zip ───────────────────────────────────────────────────────
$zipName = "PassReset-$Version.zip"
$zipPath = Join-Path $PSScriptRoot $zipName

Write-Host "`n[>>] Packaging $zipName..." -ForegroundColor Cyan

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# Copy the install script into the staging folder so a single Compress-Archive call
# produces an atomic zip. The copy is removed afterwards to keep the publish folder clean.
$installScriptDest = Join-Path $publishOut 'Install-PassReset.ps1'
Copy-Item "$PSScriptRoot\Install-PassReset.ps1" -Destination $installScriptDest

try {
    Compress-Archive -Path "$publishOut\*" -DestinationPath $zipPath -CompressionLevel Optimal
} finally {
    Remove-Item $installScriptDest -ErrorAction SilentlyContinue
}

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  [OK] $zipName ($sizeMb MB) → $zipPath" -ForegroundColor Green

Write-Host @"

Release package ready:
  $zipPath

Next steps:
  1. Copy the zip to the IIS server and extract it to a staging folder
  2. Run .\Install-PassReset.ps1 (it reads from deploy\publish\ on the build machine,
     or point -PublishFolder at the extracted folder on the server)
  3. Edit C:\inetpub\PassReset\appsettings.Production.json

"@ -ForegroundColor Yellow
