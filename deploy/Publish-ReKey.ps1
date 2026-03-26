#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the frontend and publishes the .NET app into deploy\publish\.

.DESCRIPTION
    Run this from the repo root before running Install-ReKey.ps1.
    Output goes to deploy\publish\ — the Install script expects it there.

.PARAMETER Configuration
    Build configuration. Default: Release
#>
param(
    [string] $Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot   = Split-Path -Parent $PSScriptRoot   # one level up from deploy\
$clientApp  = Join-Path $repoRoot 'src\ReKey.Web\ClientApp'
$publishOut = Join-Path $PSScriptRoot 'publish'

Write-Host "`n▶ Building React frontend..." -ForegroundColor Cyan
Push-Location $clientApp
try {
    npm ci --silent
    npm run build
} finally {
    Pop-Location
}
Write-Host "  ✓ Frontend built → src\ReKey.Web\wwwroot\" -ForegroundColor Green

Write-Host "`n▶ Publishing .NET app ($Configuration)..." -ForegroundColor Cyan
dotnet publish "$repoRoot\src\ReKey.Web\ReKey.Web.csproj" `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained false `
    --output $publishOut

Write-Host "  ✓ Published to $publishOut" -ForegroundColor Green
Write-Host "`nRun .\Install-ReKey.ps1 to deploy to IIS.`n" -ForegroundColor Yellow
