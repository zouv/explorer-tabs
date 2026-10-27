@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 完整安全自动检查
rem  build.ps1 + NativeSmoke / CrossProcessSmoke / TabUiSmoke /
rem  CreationUiSmoke / WorkspaceUiSmoke
rem  用法: test.bat [-OutputDirectory <目录>]
rem  等价命令: sh manager.sh test
rem
rem  测试不启动真实 Explorer、不显示交互宿主、不注册全局钩子。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

echo ============================================================
echo  Test   ^|  build + 全部安全 smoke
echo ============================================================

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke %*
set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" (
    echo.
    echo [FAILED] 部分检查失败。
    popd
    exit /b %EXITCODE%
)

echo.
echo [SUCCESS] 全部安全自动检查通过。
popd
exit /b 0

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1
