#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the frontend, publishes the .NET app, and packages a release zip.

.DESCRIPTION
    Run this from the repo root before running Install-ReKey.ps1.
    Output:
      deploy\publish\        — raw publish output (used by Install-ReKey.ps1)
      deploy\ReKey-<ver>.zip — release package ready for distribution

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
$clientApp  = Join-Path $repoRoot 'src\ReKey.Web\ClientApp'
$publishOut = Join-Path $PSScriptRoot 'publish'

# ── Resolve version ───────────────────────────────────────────────────────────
if (-not $Version) {
    $Version = git -C $repoRoot describe --tags --abbrev=0 2>$null
    if (-not $Version) { $Version = 'dev' }
}

# ── Frontend ──────────────────────────────────────────────────────────────────
Write-Host "`n▶ Building React frontend..." -ForegroundColor Cyan
Push-Location $clientApp
try {
    npm ci --silent
    npm run build
} finally {
    Pop-Location
}
Write-Host "  ✓ Frontend built → src\ReKey.Web\wwwroot\" -ForegroundColor Green

# ── .NET publish ──────────────────────────────────────────────────────────────
Write-Host "`n▶ Publishing .NET app ($Configuration)..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\ReKey.Web\ReKey.Web.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $publishOut
Write-Host "  ✓ Published to $publishOut" -ForegroundColor Green

# ── Package release zip ───────────────────────────────────────────────────────
$zipName = "ReKey-$Version.zip"
$zipPath = Join-Path $PSScriptRoot $zipName

Write-Host "`n▶ Packaging $zipName..." -ForegroundColor Cyan

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Compress-Archive -Path "$publishOut\*" -DestinationPath $zipPath -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "  ✓ $zipName ($sizeMb MB) → $zipPath" -ForegroundColor Green

Write-Host @"

Release package ready:
  $zipPath

Next steps:
  1. Copy the zip to the IIS server and extract it to a staging folder
  2. Run .\Install-ReKey.ps1 (it reads from deploy\publish\ on the build machine,
     or point -PublishFolder at the extracted folder on the server)
  3. Edit C:\inetpub\ReKey\appsettings.Production.json

"@ -ForegroundColor Yellow
