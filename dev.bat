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
rem  安全: 本脚本不结束、不接管任何已有 Explorer / 宿主。
rem        检测到 ExplorerTabs 正在运行时直接退出，请先手动退出旧宿主。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

tasklist /FI "IMAGENAME eq ExplorerTabs.exe" 2>nul | findstr /I /C:"ExplorerTabs.exe" >nul
if not errorlevel 1 (
    echo [ERROR] 检测到 ExplorerTabs 正在运行。
    echo         本脚本不会结束宿主或 Explorer；请先手动退出旧宿主再运行 dev.bat。
    popd
    exit /b 1
)

echo ============================================================
echo  Dev    ^|  build.ps1 + 启动开发快照
echo ============================================================

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" %*
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

:BuildFailed
echo.
echo [FAILED] 构建或基础检查失败，未启动应用。
popd
exit /b 1

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1
