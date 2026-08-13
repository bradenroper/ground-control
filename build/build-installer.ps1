<#
.SYNOPSIS
    Publishes Ground Control and compiles the Windows installer.

.DESCRIPTION
    Produces dist\GroundControl-Setup-<version>.exe.

    The app is published self-contained and single-file, so the installer works on a machine
    with no .NET runtime and drops exactly one executable into the install folder.

    Requires Inno Setup 6 (build-machine only — nothing from it ships inside the app):
        winget install JRSoftware.InnoSetup

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build\build-installer.ps1
    powershell -ExecutionPolicy Bypass -File build\build-installer.ps1 -Version 1.1.0
#>
[CmdletBinding()]
param(
    [string] $Version = '1.0.0',
    [string] $Runtime = 'win-x64',
    [switch] $SkipPublish
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root       = Resolve-Path (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) '..')
$project    = Join-Path $root 'src\GroundControl\GroundControl.csproj'
$publishDir = Join-Path $root "src\GroundControl\bin\Release\net9.0-windows\$Runtime\publish"
$distDir    = Join-Path $root 'dist'
$issPath    = Join-Path $root 'installer\GroundControl.iss'

# ---------------------------------------------------------------- publish
if (-not $SkipPublish) {
    Write-Host "==> Publishing $Runtime (self-contained, single file)" -ForegroundColor Cyan

    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

    & dotnet publish $project `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:DebugType=none `
        -p:Version=$Version `
        --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

$exe = Join-Path $publishDir 'GroundControl.exe'
if (-not (Test-Path $exe)) { throw "Published executable not found: $exe" }
Write-Host ("    {0} ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))

# ---------------------------------------------------------------- compile the installer
$isccCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup"
}

if (-not (Test-Path $distDir)) { New-Item -ItemType Directory -Path $distDir -Force | Out-Null }

Write-Host "==> Compiling installer with $iscc" -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DSourceDir=$publishDir" "/DOutputDir=$distDir" $issPath
if ($LASTEXITCODE -ne 0) { throw "ISCC failed with exit code $LASTEXITCODE" }

$setup = Join-Path $distDir "GroundControl-Setup-$Version.exe"
Write-Host ""
Write-Host ("==> {0} ({1:N1} MB)" -f $setup, ((Get-Item $setup).Length / 1MB)) -ForegroundColor Green
