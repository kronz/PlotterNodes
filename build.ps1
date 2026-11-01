<#
.SYNOPSIS
  Builds the PlotterNodes zero-touch library and the Canvas 3D node (PlotterNodes.Canvas) and
  assembles the Dynamo package folder.

.DESCRIPTION
  Three ways to reference Dynamo, in order of preference for a Dynamo 4.x built from source:

    .\build.ps1 -DynamoBinDir D:\Drawing\Dynamo\bin\AnyCPU\Release     # exact match with your local Dynamo build
    .\build.ps1                                                          # DynamoVisualProgramming.ZeroTouchLibrary from NuGet
    .\build.ps1 -Smoke                                                   # headless build + smoke test, no Dynamo binaries needed
    .\build.ps1 -Make2DTests                                             # Make2D test suite against the C# engine (needs Python + numpy)

  Add -Install to copy the assembled package into %APPDATA%\Dynamo\Dynamo Core\<version>\packages.
  Use -DotNet net8.0 for Dynamo 3.x hosts (and -DynamoNuGetVersion 3.4.2.7731 in NuGet mode).

  The Canvas 3D node (src\PlotterNodes.Canvas, WPF + HelixToolkit) builds in the -DynamoBinDir and
  NuGet modes; -SkipCanvas leaves it out. Smoke and Make2DTests never build it.

.EXAMPLE
  .\build.ps1 -DynamoBinDir D:\Drawing\Dynamo\bin\AnyCPU\Debug -Install
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$DotNet = "net10.0",
    [string]$DynamoBinDir = "",
    [string]$DynamoNuGetVersion = "",
    [string]$DynamoRepo = "",
    [switch]$Smoke,
    [switch]$Make2DTests,
    [string[]]$Only = @(),
    [switch]$Install,
    [string]$PackagesDir = "",
    [switch]$SkipCanvas
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "src\PlotterNodes\PlotterNodes.csproj"
$canvasProj = Join-Path $root "src\PlotterNodes.Canvas\PlotterNodes.Canvas.csproj"
$packageDir = Join-Path $root "package\PlotterNodes"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK (dotnet) is not on PATH. Install the SDK matching -DotNet ($DotNet): winget install Microsoft.DotNet.SDK.10"
}
$sdks = & dotnet --list-sdks 2>$null
$wanted = ($DotNet -replace '^net', '').Split('.')[0]
if (-not ($sdks | Where-Object { $_ -match "^$wanted\." })) {
    throw "No .NET $wanted SDK installed (found: $(if ($sdks) { $sdks -join ', ' } else { 'none' })). Install it: winget install Microsoft.DotNet.SDK.$wanted"
}

$props = @("-c", $Configuration, "-p:DotNet=$DotNet")
if ($DynamoNuGetVersion) { $props += "-p:DynamoNuGetVersion=$DynamoNuGetVersion" }

# Regenerate the sample graph when Python is available (optional).
$py = Get-Command python -ErrorAction SilentlyContinue
if ($py) {
    try { & $py.Source (Join-Path $root "tools\make_sample_graph.py") | Out-Null } catch { Write-Warning "Sample graph not regenerated: $_" }
    try { & $py.Source (Join-Path $root "tools\make_numeral_graphs.py") | Out-Null } catch { Write-Warning "Numeral graphs not regenerated: $_" }
    try { & $py.Source (Join-Path $root "tools\make_make2d_graph.py") | Out-Null } catch { Write-Warning "Make2D graph not regenerated: $_" }
}

if ($Make2DTests) {
    if (-not $py) { throw "The Make2D test suite needs Python 3 with numpy on PATH (matplotlib is optional)." }
    if (-not $DynamoRepo) { $DynamoRepo = Join-Path (Split-Path -Parent $root) "Dynamo" }
    $suite = Join-Path $root "tests\make2d\suite.py"
    $shim = Join-Path $root "tools\DynamoServicesShim\DynamoServicesShim.csproj"
    $runner = Join-Path $root "tools\Make2DTests\Make2DTests.csproj"
    if (-not (Test-Path (Join-Path $root "tests\make2d\cases"))) {
        Write-Host "Generating Make2D test cases and ground truth..." -ForegroundColor Cyan
        & $py.Source $suite generate
        if ($LASTEXITCODE -ne 0) { throw "Case generation failed." }
    }
    Write-Host "Running the C# engine on the test cases..." -ForegroundColor Cyan
    & dotnet run --project $runner @props "-p:DynamoServicesShim=$shim" "-p:UseDynamoGeometry=false" "-p:SkipPackage=true" "-p:DynamoRepo=$DynamoRepo" -- (Join-Path $root "tests\make2d\cases") (Join-Path $root "tests\make2d\results\csharp") @Only
    if ($LASTEXITCODE -ne 0) { Write-Warning "The engine reported errors; evaluating anyway." }
    $evalArgs = @($suite, "evaluate", "--engine", "csharp")
    if ($Only.Count -gt 0) { $evalArgs += "--only"; $evalArgs += $Only }
    & $py.Source @evalArgs
    $code = $LASTEXITCODE
    Write-Host "Report: $(Join-Path $root 'tests\make2d\results\report_csharp.html')" -ForegroundColor Green
    exit $code
}

if ($Smoke) {
    if (-not $DynamoRepo) { $DynamoRepo = Join-Path (Split-Path -Parent $root) "Dynamo" }
    if (-not (Test-Path (Join-Path $DynamoRepo "src\NodeServices\Attributes.cs"))) {
        throw "Smoke mode compiles the DynamoServices shim from a Dynamo checkout. Pass -DynamoRepo <path to Dynamo repo>."
    }
    $shim = Join-Path $root "tools\DynamoServicesShim\DynamoServicesShim.csproj"
    $smokeProj = Join-Path $root "tools\Smoke\Smoke.csproj"
    $out = Join-Path $root "smoke-output"
    Write-Host "Building and running the smoke test (shim mode, no ProtoGeometry)..." -ForegroundColor Cyan
    & dotnet run --project $smokeProj @props "-p:DynamoServicesShim=$shim" "-p:UseDynamoGeometry=false" "-p:SkipPackage=true" "-p:DynamoRepo=$DynamoRepo" -- $out
    if ($LASTEXITCODE -ne 0) { throw "Smoke test failed." }
    Write-Host "Smoke SVGs written to $out" -ForegroundColor Green
    $previewProj = Join-Path $root "tools\NumeralPreview\NumeralPreview.csproj"
    $previewOut = Join-Path $root "samples\Numerals\preview"
    Write-Host "Rendering the numeral sample graphs headlessly..." -ForegroundColor Cyan
    & dotnet run --project $previewProj @props "-p:DynamoServicesShim=$shim" "-p:UseDynamoGeometry=false" "-p:SkipPackage=true" "-p:DynamoRepo=$DynamoRepo" -- $previewOut
    if ($LASTEXITCODE -ne 0) { throw "Numeral preview failed." }
    Write-Host "Numeral previews written to $previewOut" -ForegroundColor Green
    return
}

if ($DynamoBinDir) {
    if (-not (Test-Path $DynamoBinDir)) {
        throw "Folder not found: $DynamoBinDir. Build Dynamo first, e.g. msbuild <Dynamo repo>\src\Dynamo.All.sln /p:Configuration=Release (output goes to <Dynamo repo>\bin\AnyCPU\Release)."
    }
    if (-not (Test-Path (Join-Path $DynamoBinDir "DynamoServices.dll"))) {
        throw "DynamoServices.dll not found in $DynamoBinDir. The folder exists but Dynamo has not produced its binaries there. Build Dynamo first: msbuild <Dynamo repo>\src\Dynamo.All.sln /p:Configuration=Release, then retry."
    }
    if (-not (Test-Path (Join-Path $DynamoBinDir "ProtoGeometry.dll"))) {
        Write-Warning "ProtoGeometry.dll not found in $DynamoBinDir. Building without ProtoGeometry interop (-p:UseDynamoGeometry=false)."
        $props += "-p:UseDynamoGeometry=false"
    }
    $props += "-p:DynamoBinDir=$DynamoBinDir"
    Write-Host "Building against local Dynamo binaries in $DynamoBinDir" -ForegroundColor Cyan
} else {
    Write-Host "Building against the DynamoVisualProgramming.ZeroTouchLibrary NuGet package" -ForegroundColor Cyan
}

& dotnet build $proj @props
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

# Canvas 3D node: NodeModel + WPF view customization, references DynamoCoreWpf and HelixToolkit.
if (-not $SkipCanvas) {
    $canvasProps = @("-c", $Configuration, "-p:DotNet=$DotNet")
    if ($DynamoNuGetVersion) { $canvasProps += "-p:DynamoNuGetVersion=$DynamoNuGetVersion" }
    $buildCanvas = $true
    if ($DynamoBinDir) {
        foreach ($needed in @("DynamoCoreWpf.dll", "HelixToolkit.SharpDX.Core.Wpf.dll", "SharpDX.D3DCompiler.dll")) {
            if (-not (Test-Path (Join-Path $DynamoBinDir $needed))) {
                Write-Warning "$needed not found in $DynamoBinDir; skipping the Canvas 3D node. Build Dynamo.All.sln (not only DynamoCore.sln) or use NuGet mode."
                $buildCanvas = $false
            }
        }
        $canvasProps += "-p:DynamoBinDir=$DynamoBinDir"
    }
    if ($buildCanvas) {
        Write-Host "Building the Canvas 3D node (PlotterNodes.Canvas)" -ForegroundColor Cyan
        & dotnet build $canvasProj @canvasProps
        if ($LASTEXITCODE -ne 0) { throw "Canvas 3D build failed. Use -SkipCanvas to build PlotterNodes alone." }
    }
}

# Safety: a Dynamo package must never ship Dynamo's own assemblies (or Helix/SharpDX, which Dynamo loads).
$forbiddenList = @("DynamoServices.dll", "ProtoGeometry.dll", "DynamoCore.dll", "DynamoUnits.dll", "DynamoCoreWpf.dll", "DynamoUtilities.dll", "ProtoCore.dll", "Newtonsoft.Json.dll")
foreach ($forbidden in $forbiddenList) {
    $f = Join-Path $packageDir "bin\$forbidden"
    if (Test-Path $f) { Remove-Item $f -Force; Write-Warning "Removed $forbidden from the package bin folder." }
}
Get-ChildItem (Join-Path $packageDir "bin") -Filter "*.dll" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "HelixToolkit*.dll" -or $_.Name -like "SharpDX*.dll" } |
    ForEach-Object { Remove-Item $_.FullName -Force; Write-Warning "Removed $($_.Name) from the package bin folder." }

Write-Host "Package assembled at $packageDir" -ForegroundColor Green

if ($Install) {
    if (-not $PackagesDir) {
        $coreRoot = Join-Path $env:APPDATA "Dynamo\Dynamo Core"
        $versions = Get-ChildItem $coreRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending
        if (-not $versions) { throw "No Dynamo Core version folder found under $coreRoot. Pass -PackagesDir explicitly." }
        $PackagesDir = Join-Path $versions[0].FullName "packages"
    }
    $dest = Join-Path $PackagesDir "PlotterNodes"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Path (Join-Path $packageDir "*") -Destination $dest -Recurse -Force
    Write-Host "Installed to $dest (restart Dynamo to load it)" -ForegroundColor Green
}
