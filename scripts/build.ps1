param(
    [string]$OutputDirectory = ".runtime\build\ExplorerTabs-dev",
    [switch]$NativeSmoke,
    [switch]$CrossProcessSmoke,
    [switch]$TabUiSmoke,
    [switch]$CreationUiSmoke,
    [switch]$WorkspaceUiSmoke,
    [switch]$TreatWarningsAsErrors,
    [switch]$CloseRunningHost
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
# dev.bat opts into an explicit confirmation before bypassing the running-host guard.
# Never close a host silently: list it, default to cancel, and only continue on Y.
if ($CloseRunningHost) {
    $running = @(Get-Process -Name ExplorerTabs -ErrorAction SilentlyContinue)
    if ($running.Count -gt 0) {
        Write-Host ""
        Write-Host "[WARN] 检测到 ExplorerTabs 正在运行：" -ForegroundColor Yellow
        foreach ($process in $running) {
            $processPath = $null
            try { $processPath = $process.Path } catch { }
            if ([string]::IsNullOrEmpty($processPath)) { $processPath = '(无法读取路径)' }
            Write-Host ("       PID {0}  {1}" -f $process.Id, $processPath)
        }
        Write-Host "       关闭宿主后，已嵌入的标签窗口可能一并关闭且无法恢复。"
        if ([Console]::IsInputRedirected) {
            throw "ExplorerTabs is still running. Run dev.bat from an interactive console so you can confirm closing it."
        }
        $answer = Read-Host "是否强制关闭并继续构建？输入 Y 确认，其它任意输入取消"
        if ($answer -notmatch '^\s*[Yy]([Ee][Ss])?\s*$') {
            Write-Host "[INFO] 已取消：未关闭任何进程，也未启动应用。" -ForegroundColor Yellow
            exit 2
        }
        foreach ($process in $running) {
            try { $null = $process.CloseMainWindow() } catch { }
        }
        # Give the normal close (session save / settings / detach) a bounded chance first.
        $deadline = (Get-Date).AddSeconds(12)
        while ((Get-Process -Name ExplorerTabs -ErrorAction SilentlyContinue) -and (Get-Date) -lt $deadline) {
            Start-Sleep -Milliseconds 250
        }
        foreach ($process in @(Get-Process -Name ExplorerTabs -ErrorAction SilentlyContinue)) {
            try { $process.Kill(); $process.WaitForExit(5000) } catch { }
        }
        if (@(Get-Process -Name ExplorerTabs -ErrorAction SilentlyContinue).Count -gt 0) {
            throw "无法关闭运行中的 ExplorerTabs（可能需要管理员权限）。请手动退出后重试。"
        }
        Write-Host "[INFO] 已关闭运行中的 ExplorerTabs，继续构建。" -ForegroundColor Green
    }
}
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
