# Isolated fake packaging: no host process lookup/exit, no writes to real dist.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PackageTransaction.ps1')
$fixture = Assert-PackagePath (Join-Path $root ('.runtime\package-tests-' + [Guid]::NewGuid().ToString('N'))) $root
New-Item -ItemType Directory -Force -Path $fixture | Out-Null
$checks = 0
function Check-Package([bool]$condition, [string]$message) {
    if (-not $condition) { throw "FAIL: $message" }
    $script:checks++; Write-Host "PASS $message"
}
function New-Fixture([string]$name) {
    $case = Join-Path $fixture $name
    foreach ($dir in @('scripts', 'dist\dev', 'dist\release', 'dist\v-old-release', 'src\ExplorerTabs.App\bin\Debug\net48', 'src\ExplorerTabs.App\bin\Release\net48', 'assets\icons', 'assets\screenshots')) {
        New-Item -ItemType Directory -Path (Join-Path $case $dir) -Force | Out-Null
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'package.ps1'), (Join-Path $PSScriptRoot 'PackageTransaction.ps1') -Destination (Join-Path $case 'scripts')
    foreach ($config in @('Debug', 'Release')) {
        foreach ($file in @('ExplorerTabs.exe', 'ExplorerTabs.exe.config', 'ExplorerTabs.Core.dll', 'ExplorerTabs.pdb', 'ExplorerTabs.Core.pdb')) {
            Copy-Item -LiteralPath (Join-Path $root "src\ExplorerTabs.App\bin\Release\net48\$file") -Destination (Join-Path $case "src\ExplorerTabs.App\bin\$config\net48\$file")
        }
    }
    foreach ($file in @('README.md', 'assets\icons\explorer-tabs-a.svg', 'assets\screenshots\main-window.png', 'assets\screenshots\multi-row.png')) {
        [IO.File]::WriteAllText((Join-Path $case $file), 'fixture-only')
    }
    foreach ($dir in @('dev', 'release', 'v-old-release')) { [IO.File]::WriteAllText((Join-Path $case "dist\$dir\old.txt"), "old-$dir") }
    return $case
}
function Invoke-Fixture([string]$case, [string]$version, [bool]$buildFailure = $false) {
    & {
        param($case, $fakeVersion, $failBuild)
        # Never call real process enumeration or dotnet from a copied package script.
        function Get-Process { return @() }
        function dotnet {
            if ($args[0] -eq 'build') { throw 'injected build failure' }
            if ($args[0] -eq '--version') { return 'fixture-sdk' }
            return $fakeVersion
        }
        & (Join-Path $case 'scripts\package.ps1') -SkipBuild:(-not $failBuild)
    } $case $version $buildFailure
}
$version = [Reflection.AssemblyName]::GetAssemblyName((Join-Path $root 'src\ExplorerTabs.App\bin\Release\net48\ExplorerTabs.exe')).Version.ToString(3)
try {
    foreach ($reason in @('missing', 'stale', 'build', 'zip')) {
        $case = New-Fixture $reason
        $expectedVersion = $version
        if ($reason -eq 'missing') { Remove-Item -LiteralPath (Join-Path $case 'src\ExplorerTabs.App\bin\Debug\net48\ExplorerTabs.exe') }
        if ($reason -eq 'stale') { $expectedVersion = '9.9.9' }
        if ($reason -eq 'zip') {
            Add-Content -LiteralPath (Join-Path $case 'scripts\PackageTransaction.ps1') -Value 'function Test-PackageZip { throw "injected zip validation failure" }'
        }
        $failed = $false
        try { Invoke-Fixture $case $expectedVersion ($reason -eq 'build') } catch {
            $expectedError = @{ missing = 'Missing build output'; stale = 'Stale build version'; build = 'injected build failure'; zip = 'injected zip validation failure' }[$reason]
            if (-not $_.Exception.Message.Contains($expectedError)) { throw }
            $failed = $true
        }
        Check-Package $failed "$reason failure detected"
        foreach ($dir in @('dev', 'release', 'v-old-release')) {
            Check-Package (([IO.File]::ReadAllText((Join-Path $case "dist\$dir\old.txt"))) -eq "old-$dir") "$reason retains $dir bytes"
        }
    }
    $case = New-Fixture 'success'; Invoke-Fixture $case $version
    Check-Package ((Test-Path -LiteralPath (Join-Path $case "dist\release\ExplorerTabs_$($version)_x64-unpack.zip")) -and (Test-Path -LiteralPath (Join-Path $case 'dist\release\unpack\ExplorerTabs.exe'))) 'success publishes complete release'
    Check-Package ((Test-Path -LiteralPath (Join-Path $case "dist\dev\ExplorerTabs_$($version)_x64-unpack-debug.zip")) -and -not (Test-Path -LiteralPath (Join-Path $case 'dist\dev\old.txt'))) 'success replaces dev only after validation'
    Check-Package (Test-Path -LiteralPath (Join-Path $case 'dist\v-old-release\old.txt')) 'history survives success'
    Check-Package (@(Get-ChildItem -LiteralPath (Join-Path $case '.runtime\staging') -Directory).Count -eq 0) 'success cleans only own staging directory'
    # Inject second promotion failure after dev has already moved: restore both directories.
    $case = New-Fixture 'rollback'; $stage = Join-Path $case 'stage'
    foreach ($dir in @('dev', 'release')) {
        New-Item -ItemType Directory -Force -Path (Join-Path $stage $dir) | Out-Null
        [IO.File]::WriteAllText((Join-Path $stage "$dir\new.txt"), 'new')
    }
    $script:promotionFailed = $false
    & {
        param($case, $stage)
        function Move-Item {
            param([string]$LiteralPath, [string]$Destination)
            if ($LiteralPath -eq (Join-Path $stage 'release')) { throw 'injected second promotion failure' }
            Microsoft.PowerShell.Management\Move-Item -LiteralPath $LiteralPath -Destination $Destination
        }
        try { Publish-PackageSet $case $stage (Join-Path $case 'dist') } catch { $script:promotionFailed = $true }
    } $case $stage
    Check-Package $script:promotionFailed 'second promotion failure detected'
    foreach ($dir in @('dev', 'release')) { Check-Package (([IO.File]::ReadAllText((Join-Path $case "dist\$dir\old.txt"))) -eq "old-$dir") "transaction rolls back $dir" }
    $escape = $false; try { Assert-PackagePath (Join-Path $fixture '..\..\escape') $fixture | Out-Null } catch { $escape = $true }
    Check-Package $escape 'workspace escape rejected before filesystem mutation'
    $badZip = Join-Path $fixture 'bad.zip'; [IO.File]::WriteAllText($badZip, 'not-a-zip')
    $bad = $false; try { Test-PackageZip (Join-Path $case 'dist\dev') $badZip '' } catch { $bad = $true }
    Check-Package $bad 'corrupt ZIP rejected'
    Write-Host "PASS: $checks isolated package checks. Real dist and running hosts untouched."
} finally {
    $cleanup = Assert-PackagePath $fixture $root
    Remove-Item -LiteralPath $cleanup -Recurse -Force
}
