#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

wsl_to_windows_path() {
  local path="$1"
  if [[ "$path" =~ ^/mnt/([a-zA-Z])/(.*)$ ]]; then
    local drive="${BASH_REMATCH[1]}"
    local rest="${BASH_REMATCH[2]}"
    rest="${rest//\//\\}"
    printf '%s:\\%s' "${drive^^}" "$rest"
    return 0
  fi
  printf '%s' "$path"
}

bundle_windows_dir="$(wsl_to_windows_path "$script_dir")"
entry_script="${OPENCLAW_WIN_ADMIN_WSL_ENTRY:-${bundle_windows_dir}\\openclaw-win-admin-wsl-entry.ps1}"

exec powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$entry_script" "$@"
