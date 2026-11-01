<#
.SYNOPSIS
  Zips the assembled Dynamo package and optionally publishes it as a GitHub Release.
.EXAMPLE
  .\tools\make_release.ps1            # builds dist\PlotterNodes-<version>.zip
  .\tools\make_release.ps1 -Publish   # also tags v<version> and creates the release (requires gh CLI)
#>
param([switch]$Publish)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$pkgJson = Join-Path $root "package\PlotterNodes\pkg.json"
$version = (Get-Content $pkgJson -Raw | ConvertFrom-Json).version

& (Join-Path $root "build.ps1")

$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force $dist | Out-Null
$zip = Join-Path $dist "PlotterNodes-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $root "package\PlotterNodes") -DestinationPath $zip
Write-Host "Created $zip" -ForegroundColor Green

if ($Publish) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) not found. Install it or upload the zip manually." }
    gh release create "v$version" $zip --title "PlotterNodes $version" --generate-notes
}
