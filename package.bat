@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  ExplorerTabs - 打包脚本（dev + release）
rem  用法: package.bat
rem  等价命令: sh manager.sh package
rem
rem  产物（dist\ 已 gitignore，历史 dist\vX-release 目录原样保留）:
rem    dist\dev\ExplorerTabs_<版本>_x64-unpack-debug.zip
rem    dist\release\unpack\                         （可直接运行）
rem    dist\release\ExplorerTabs_<版本>_x64-unpack.zip  （zip 内无顶层目录）
rem
rem  说明:
rem    - 版本号单一来源: Directory.Build.props 的 <Version>。
rem    - 不生成 setup.exe: 本项目约束为无第三方 NuGet，release 以免安装
rem      解压包分发；不引入 Inno Setup / IExpress。
rem    - 不终止任何运行中的 ExplorerTabs，也不关闭用户 Explorer。若打包
rem      目标正在运行，脚本会报错并要求先手动退出。
rem ============================================================

pushd "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 goto :NoPowerShell

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\package.ps1" %*
set "EXITCODE=%ERRORLEVEL%"
popd
exit /b %EXITCODE%

:NoPowerShell
echo [ERROR] 未找到 powershell.exe。
popd
exit /b 1
