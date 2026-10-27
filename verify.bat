@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 提交前质量闸门
rem  build.ps1（警告即错误 + 全部安全 smoke）-> 文档体检 check-docs.sh
rem  用法: verify.bat
rem  参考命令: sh manager.sh test 后再 bash check-docs.sh（本脚本额外启用警告即错误）
rem
rem  文档体检是 bash 脚本：有 Git for Windows / WSL 才跑得起来，
rem  没有就只提示不失败。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

echo ============================================================
echo  Verify ^|  build -TreatWarningsAsErrors + 全部 smoke + check-docs
echo ============================================================

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" -TreatWarningsAsErrors -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke %*
if errorlevel 1 goto :BuildOrTestFailed

rem 先找 sh，再退而求其次找 bash（Git for Windows 默认只把 Git\cmd 加进 PATH，
rem 那里有 git.exe 但未必有 sh.exe）。
where sh >nul 2>&1
if not errorlevel 1 goto :RunDocCheckSh
where bash >nul 2>&1
if not errorlevel 1 goto :RunDocCheckBash
goto :NoSh

:RunDocCheckSh
sh check-docs.sh
if errorlevel 1 goto :DocsFailed
goto :Done

:RunDocCheckBash
bash check-docs.sh
if errorlevel 1 goto :DocsFailed
goto :Done

:NoSh
echo.
echo [WARN] 未找到 sh / bash，跳过文档体检（check-docs.sh）。
echo        装了 Git for Windows 或 WSL 后重跑本脚本即可，命令为：sh check-docs.sh

:Done
echo.
echo [SUCCESS] 全部通过。
popd
exit /b 0

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1

:BuildOrTestFailed
echo.
echo [FAILED] 构建 / 安全检查失败（TreatWarningsAsErrors：警告即错误）。
popd
exit /b 1

:DocsFailed
echo.
echo [FAILED] 文档体检未通过。
popd
exit /b 1
