#!/usr/bin/env bash
set -euo pipefail
cd -- "$(dirname -- "$0")"
case "${1:-help}" in
  build) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 ;;
  test) powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -NativeSmoke -CrossProcessSmoke -TabUiSmoke -CreationUiSmoke -WorkspaceUiSmoke ;;
  run) powershell.exe -NoProfile -Command 'Start-Process -FilePath "dist/v0.6.2-release/ExplorerTabs.exe"' ;;
  check-docs) bash check-docs.sh ;;
  help) printf '%s\n' 'manager.sh build | test | run (interactive, explicit only) | check-docs' ;;
  *) printf '%s\n' 'Unknown command' >&2; exit 2 ;;
esac
