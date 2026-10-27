@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 显式启动脚本（真实交互宿主）
rem  用法: run.bat [release|dev]
rem        默认 release = dist\release\unpack\ExplorerTabs.exe
rem        dev     = .runtime\build\ExplorerTabs-dev\ExplorerTabs.exe
rem  等价命令: sh manager.sh run（仅 release）
rem
rem  安全: 仅由用户手动运行；本脚本不会接管、关闭或结束已有 Explorer / 宿主。
rem        运行前请先正常退出旧宿主。
rem ============================================================

pushd "%~dp0"

set "MODE=%~1"
if "%MODE%"=="" set "MODE=release"

if /I "%MODE%"=="release" (
    set "EXE=dist\release\unpack\ExplorerTabs.exe"
    set "HINT=先运行 package.bat 生成正式包"
) else if /I "%MODE%"=="dev" (
    set "EXE=.runtime\build\ExplorerTabs-dev\ExplorerTabs.exe"
    set "HINT=先运行 build.bat 生成开发快照"
) else (
    echo [ERROR] 未知模式：%MODE%
    echo         用法: run.bat [release^|dev]
    popd
    exit /b 2
)

if not exist "%EXE%" (
    echo [ERROR] 未找到 %EXE%
    echo         %HINT%。
    popd
    exit /b 1
)

echo [INFO] 启动 %EXE%
echo        这是真实交互宿主；请先确认旧宿主已正常退出。
start "" "%EXE%"
popd
exit /b 0
