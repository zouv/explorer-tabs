@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 构建脚本（构建 + 基础单元检查）
rem  用法: build.bat [输出目录]
rem        例如 build.bat .runtime\build\ExplorerTabs-dev
rem  等价命令: sh manager.sh build
rem
rem  说明: 实际逻辑在 scripts\build.ps1；输出目录可省略，
rem        默认 .runtime\build\ExplorerTabs-dev（已 gitignore）。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

echo ============================================================
echo  Build  ^|  scripts\build.ps1
echo ============================================================

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
    echo.
    echo [FAILED] 构建或基础检查失败。
    popd
    exit /b %EXITCODE%
)

echo.
echo [SUCCESS] 构建完成。
popd
exit /b 0

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1
