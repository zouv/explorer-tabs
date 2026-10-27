@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 清理脚本
rem  移除 src\*\bin、src\*\obj、tests\*\bin、tests\*\obj 与 .runtime；
rem  保留 dist 成品包（含 dist\vX-release 历史包）与 artifacts 验证产物。
rem  用法: clean.bat
rem  说明: manager.sh 暂无 clean 子命令；Windows 下直接运行本脚本。
rem
rem  安全: 本脚本绝不 taskkill。检测到 ExplorerTabs 正在运行时直接退出，
rem        请手动退出宿主后再清理（避免部分删除正在使用的文件）。
rem ============================================================

pushd "%~dp0"

echo ============================================================
echo  Clean  ^|  移除 bin / obj / .runtime（保留 dist / artifacts）
echo ============================================================

tasklist /FI "IMAGENAME eq ExplorerTabs.exe" 2>nul | findstr /I /C:"ExplorerTabs.exe" >nul
if not errorlevel 1 (
    echo [ERROR] 检测到 ExplorerTabs 正在运行。
    echo         本脚本不会结束宿主或 Explorer；请手动退出后再运行 clean.bat。
    popd
    exit /b 1
)

set "FAILED=0"
for /d %%D in ("src\*") do call :CleanProject "%%~D"
for /d %%D in ("tests\*") do call :CleanProject "%%~D"

if exist ".runtime" (
    rmdir /S /Q ".runtime" 2>nul
    if exist ".runtime" (
        echo [WARN] 无法完全删除 .runtime，可能有文件被占用。
        set "FAILED=1"
    ) else (
        echo  - .runtime
    )
)

if "%FAILED%"=="1" (
    echo.
    echo [FAILED] 清理未完全成功，请检查被占用的文件。
    popd
    exit /b 1
)

echo.
echo [SUCCESS] 清理完成（dist 成品包与 artifacts 已保留）。
popd
exit /b 0

:CleanProject
set "P=%~1"
if exist "%P%\bin" (
    rmdir /S /Q "%P%\bin" 2>nul
    if exist "%P%\bin" ( echo [WARN] 无法删除 %P%\bin & set "FAILED=1" ) else ( echo  - %P%\bin )
)
if exist "%P%\obj" (
    rmdir /S /Q "%P%\obj" 2>nul
    if exist "%P%\obj" ( echo [WARN] 无法删除 %P%\obj & set "FAILED=1" ) else ( echo  - %P%\obj )
)
exit /b 0
