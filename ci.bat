@echo off
chcp 65001 >nul
setlocal EnableExtensions

rem ============================================================
rem  ExplorerTabs - 一键 CI 流水线
rem  clean -> verify（警告即错误构建 + 全部安全 smoke + 文档体检）-> package
rem  用法: ci.bat
rem
rem  注意: 会重建 dist\dev 与 dist\release；运行中的 ExplorerTabs 会使流程中止。
rem        本脚本不执行 git 提交 / 推送，也不结束任何宿主或 Explorer。
rem ============================================================

pushd "%~dp0"

call "%~dp0clean.bat"   || goto :Fail
call "%~dp0verify.bat"  || goto :Fail
call "%~dp0package.bat" || goto :Fail

echo.
echo ============================================================
echo  [SUCCESS] CI pipeline completed.
echo  Dev    : %CD%\dist\dev\
echo  Unpack : %CD%\dist\release\unpack\
echo  Zip    : %CD%\dist\release\
echo ============================================================
popd
exit /b 0

:Fail
echo.
echo [FAILED] CI pipeline aborted.
popd
exit /b 1
