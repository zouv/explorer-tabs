#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
case "${1:-help}" in
  build) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 ;;
  test) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke ;;
  package) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package.ps1 ;;
  run) powershell.exe -NoProfile -Command 'Start-Process -FilePath "dist/release/unpack/ExplorerTabs.exe"' ;;
  check-docs) bash check-docs.sh ;;
  help) cat <<'EOF'
ExplorerTabs — manager.sh

用法：
  sh manager.sh <command>

命令：
  build       构建 Debug / Release 产物
  test        完整安全自动检查（不启动真实 Explorer / 交互宿主 / 全局钩子）
  package     打包免安装 zip 到 dist/
  run         启动已打包的免安装版（显式交互，仅用户要求时）
  check-docs  文档体系机械体检
  help        显示本帮助
EOF
    ;;
  *) printf '%s\n' 'Unknown command' >&2; exit 2 ;;
esac
