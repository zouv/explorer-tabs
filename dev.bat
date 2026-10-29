@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 开发构建并启动
rem  用法: dev.bat
rem        编译（含基础单元检查）后启动开发快照：
rem        .runtime\build\ExplorerTabs-dev\ExplorerTabs.exe
rem
rem  说明: 实际构建逻辑仍在 scripts\build.ps1；参数会原样透传
rem        （例如 dev.bat -TreatWarningsAsErrors）。
rem
rem  安全: 检测到 ExplorerTabs 正在运行时先列出进程并询问；只有输入 Y
rem        才关闭（先尝试正常关闭，12 秒超时后强制结束）并继续构建 /
rem        启动，其它输入取消。不会自动关闭任何宿主或 Explorer。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

echo ============================================================
echo  Dev    ^|  build.ps1 + 启动开发快照
echo ============================================================

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -CloseRunningHost %*
if errorlevel 2 goto :Cancelled
if errorlevel 1 goto :BuildFailed

set "EXE=%~dp0.runtime\build\ExplorerTabs-dev\ExplorerTabs.exe"
if not exist "%EXE%" (
    echo [ERROR] 未找到开发快照：%EXE%
    echo         请确认构建输出目录为默认的 .runtime\build\ExplorerTabs-dev。
    popd
    exit /b 1
)

echo.
echo [INFO] 启动 %EXE%
echo        这是真实交互宿主；请仅在你自己的桌面上使用。
start "" "%EXE%"
popd
exit /b 0

:Cancelled
echo.
echo [INFO] 已取消启动：未关闭任何进程，也未启动应用。
popd
exit /b 2

:BuildFailed
echo.
echo [FAILED] 构建或基础检查失败，未启动应用。
popd
exit /b 1

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1
