param(
    [string]$OutputDirectory = ".runtime\build\ExplorerTabs-dev",
    [switch]$NativeSmoke,
    [switch]$CrossProcessSmoke,
    [switch]$TabUiSmoke,
    [switch]$CreationUiSmoke,
    [switch]$WorkspaceUiSmoke,
    [switch]$TreatWarningsAsErrors
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)).TrimEnd([IO.Path]::DirectorySeparatorChar)
# Validate before compiling or copying. Never overwrite the original prototype directory.
if (-not $destination.StartsWith($root + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Output directory must stay inside the ExplorerTabs project.'
}
if ([string]::Equals($destination, (Join-Path $root 'dist'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The original dist directory is reserved. Choose a versioned subdirectory.'
}
$targetExe = Join-Path $destination 'ExplorerTabs.exe'
if (Test-Path -LiteralPath $targetExe) {
    foreach ($running in @(Get-Process -Name ExplorerTabs -ErrorAction SilentlyContinue)) {
        $runningPath = $null
        try { $runningPath = $running.Path } catch { }
        if ([string]::IsNullOrEmpty($runningPath)) {
            throw 'Cannot verify a running host path. Choose a new OutputDirectory instead of overwriting an existing release.'
        }
        if ([string]::Equals($runningPath, $targetExe, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The target release is running. Detach and exit it, or choose a new OutputDirectory.'
        }
    }
}
$buildArguments = @((Join-Path $root 'ExplorerTabs.sln'), '-c', 'Release', '--nologo')
if ($TreatWarningsAsErrors) { $buildArguments += '-p:TreatWarningsAsErrors=true' }
& dotnet build @buildArguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$tests = Join-Path $root 'tests\ExplorerTabs.Tests\bin\Release\net48\ExplorerTabs.Tests.exe'
& $tests
if ($LASTEXITCODE -ne 0) { throw 'Unit checks failed.' }
if ($NativeSmoke) {
    & $tests --native-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Native smoke failed.' }
}
if ($CrossProcessSmoke) {
    & $tests --cross-process-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Cross-process smoke failed.' }
}
if ($TabUiSmoke) {
    & $tests --tab-ui-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Tab UI smoke failed.' }
}
if ($CreationUiSmoke) {
    & $tests --creation-ui-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Creation UI smoke failed.' }
}
if ($WorkspaceUiSmoke) {
    & $tests --workspace-ui-smoke
    if ($LASTEXITCODE -ne 0) { throw 'Workspace UI smoke failed.' }
}
$source = Join-Path $root 'src\ExplorerTabs.App\bin\Release\net48'
New-Item -ItemType Directory -Force -Path $destination | Out-Null
# Copy only known build outputs; never recursively delete or move the user's files.
foreach ($name in @('ExplorerTabs.exe', 'ExplorerTabs.exe.config', 'ExplorerTabs.Core.dll', 'ExplorerTabs.pdb', 'ExplorerTabs.Core.pdb')) {
    $file = Join-Path $source $name
    if (Test-Path -LiteralPath $file) { Copy-Item -LiteralPath $file -Destination (Join-Path $destination $name) -Force }
}
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $destination 'README.md') -Force
Write-Host "Ready: $(Join-Path $destination 'ExplorerTabs.exe')"
