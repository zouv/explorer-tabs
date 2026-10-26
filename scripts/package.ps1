param(
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'ExplorerTabs.sln'
$appProject = Join-Path $root 'src\ExplorerTabs.App\ExplorerTabs.App.csproj'
$appOutput = Join-Path $root 'src\ExplorerTabs.App\bin'
$distDir = Join-Path $root 'dist'
$devDir = Join-Path $distDir 'dev'
$releaseDir = Join-Path $distDir 'release'
$stagingDir = Join-Path $root '.runtime\staging'

# Version is single-sourced from Directory.Build.props (imported by every project).
$version = (& dotnet msbuild $appProject -getProperty:Version -nologo 2>$null | Select-Object -Last 1)
if ($null -ne $version) { $version = $version.Trim() }
if ([string]::IsNullOrEmpty($version) -or $version -notmatch '^\d+\.\d+\.\d+') {
    throw "Cannot read <Version> from Directory.Build.props via $appProject."
}

$devUnpack = Join-Path $stagingDir "dev\ExplorerTabs_${version}_x64-unpack-debug"
$devLogs = Join-Path $devUnpack 'logs'
$devZip = Join-Path $devDir "ExplorerTabs_${version}_x64-unpack-debug.zip"
$relUnpack = Join-Path $releaseDir 'unpack'
$relZip = Join-Path $releaseDir "ExplorerTabs_${version}_x64-unpack.zip"

# Never terminate a host. A packaged instance may have attached real Explorer
# windows, so refuse to overwrite a release that is currently running.
foreach ($process in @(Get-Process -Name ExplorerTabs -ErrorAction SilentlyContinue)) {
    $processPath = $null
    try { $processPath = $process.Path } catch { }
    if ([string]::IsNullOrEmpty($processPath)) {
        throw "Cannot verify the path of running ExplorerTabs (PID $($process.Id)). Exit it before packaging."
    }
    if ($processPath.StartsWith($distDir + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "A packaged ExplorerTabs is running from $processPath. Exit it before packaging; this script never terminates hosts."
    }
}

if (Test-Path -LiteralPath (Join-Path $root 'LICENSE')) {
    $license = Join-Path $root 'LICENSE'
} else {
    $license = $null
}

Write-Host "Package | Version = $version"
Write-Host "  dev zip : $devZip"
Write-Host "  unpack  : $relUnpack"
Write-Host "  zip     : $relZip"

# Only clear this package set. Historical dist/vX-release directories are kept.
foreach ($path in @($devDir, $releaseDir, $stagingDir)) {
    if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $devLogs, $relUnpack, $devDir, $releaseDir | Out-Null

function Invoke-Build([string]$configuration, [string]$log) {
    if ($SkipBuild) { return }
    & dotnet build $solution -c $configuration --nologo -v:minimal 2>&1 | Out-File -LiteralPath $log -Encoding utf8
    if ($LASTEXITCODE -ne 0) {
        Get-Content -LiteralPath $log -Tail 40
        throw "$configuration build failed. Log: $log"
    }
}

function Copy-AppFiles([string]$source, [string]$destination, [bool]$includeSymbols) {
    $names = @('ExplorerTabs.exe', 'ExplorerTabs.exe.config', 'ExplorerTabs.Core.dll')
    if ($includeSymbols) { $names += @('ExplorerTabs.pdb', 'ExplorerTabs.Core.pdb') }
    foreach ($name in $names) {
        $file = Join-Path $source $name
        if (-not (Test-Path -LiteralPath $file)) { throw "Missing build output: $file" }
        Copy-Item -LiteralPath $file -Destination (Join-Path $destination $name) -Force
    }
}

function Write-BuildInfo([string]$path) {
    $git = 'unknown'
    try { $git = (& git -C $root rev-parse --short HEAD 2>$null | Select-Object -First 1) } catch { }
    $sdk = (& dotnet --version 2>$null | Select-Object -First 1)
    @(
        "ExplorerTabs $version (dev)"
        "Built: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
        "Git: $git"
        "Dotnet: $sdk"
        "OS: $([Environment]::OSVersion.VersionString)"
    ) | Set-Content -LiteralPath $path -Encoding UTF8
}

# Create a zip whose entries are relative to $source; $prefix adds a top-level
# folder (dev keeps one, release does not, matching the release convention).
function New-Zip([string]$source, [string]$zipPath, [string]$prefix) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    $archive = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        $base = [IO.Path]::GetFullPath($source).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File) {
            $relative = $file.FullName.Substring($base.Length).Replace('\', '/')
            $entry = if ([string]::IsNullOrEmpty($prefix)) { $relative } else { "$prefix/$relative" }
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry) | Out-Null
        }
    } finally {
        $archive.Dispose()
    }
}

# 1) dev: Debug build with PDB, build log and build metadata for troubleshooting.
Write-Host "[1/4] Debug build..."
Invoke-Build 'Debug' (Join-Path $devLogs 'dev-build.log')
Copy-AppFiles (Join-Path $appOutput 'Debug\net48') $devUnpack $true
if ($license) { Copy-Item -LiteralPath $license -Destination (Join-Path $devUnpack 'LICENSE') -Force }

@(
    "ExplorerTabs $version (Debug, x64)"
    "================================="
    ""
    "Debug build for development and troubleshooting."
    "Requires Windows 10 1703+ x64 and .NET Framework 4.8 (included with Windows 10/11)."
    ""
    "Debug info: ExplorerTabs.pdb, ExplorerTabs.Core.pdb, build-info.txt, logs\"
    "User data: %LOCALAPPDATA%\ExplorerTabHost (legacy identity is kept on purpose)."
) | Set-Content -LiteralPath (Join-Path $devUnpack 'README.txt') -Encoding UTF8
Write-BuildInfo (Join-Path $devUnpack 'build-info.txt')

# 2) release: Release build without PDB, runnable in place.
Write-Host "[2/4] Release build..."
Invoke-Build 'Release' (Join-Path $devLogs 'release-build.log')
Copy-AppFiles (Join-Path $appOutput 'Release\net48') $relUnpack $false
if ($license) { Copy-Item -LiteralPath $license -Destination (Join-Path $relUnpack 'LICENSE') -Force }
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $relUnpack 'README.md') -Force
# README references these assets with relative paths; include them so the
# packaged README also renders offline.
foreach ($relative in @('assets\icons\explorer-tabs-a.svg', 'assets\screenshots\main-window.png', 'assets\screenshots\multi-row.png')) {
    $target = Join-Path $relUnpack $relative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $target -Force
}

@(
    "ExplorerTabs $version (x64)"
    "==========================="
    ""
    "Portable release: double-click ExplorerTabs.exe. No installer or admin rights needed."
    "Requires Windows 10 1703+ x64 and .NET Framework 4.8 (included with Windows 10/11)."
    ""
    "See README.md for usage. User data: %LOCALAPPDATA%\ExplorerTabHost"
    "(legacy identity is kept on purpose)."
) | Set-Content -LiteralPath (Join-Path $relUnpack 'README.txt') -Encoding UTF8

# 3) Zip: dev keeps its folder root; release entries sit directly at zip root.
Write-Host "[3/4] Creating dev zip..."
New-Zip $devUnpack $devZip "ExplorerTabs_${version}_x64-unpack-debug"

# 4) Release zip and cleanup. Staging is removed only after both zips succeed;
#    dist\release\unpack is the official runnable output and stays.
Write-Host "[4/4] Creating release zip..."
New-Zip $relUnpack $relZip ''
Remove-Item -LiteralPath $stagingDir -Recurse -Force

Write-Host ""
Write-Host "Output files:"
foreach ($file in Get-ChildItem -LiteralPath $devDir, $releaseDir -File | Sort-Object FullName) {
    Write-Host ("  {0}  {1} bytes" -f $file.FullName.Substring($distDir.Length + 1), $file.Length)
}
Write-Host ""
Write-Host "[SUCCESS] Package complete (version $version)."
Write-Host "  dev    : $devZip"
Write-Host "  unpack : $relUnpack"
Write-Host "  zip    : $relZip"
