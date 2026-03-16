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

if [[ "${1:-}" == "dashboard" ]]; then
  token="$(powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$entry_script" config get gateway.auth.token)"
  token="${token//$'\r'/}"
  token="${token//$'\n'/}"
  url="http://127.0.0.1:18790/#token=${token}"
  explorer.exe "$url" >/dev/null 2>&1 || true
  printf 'dashboard_url=%s\n' "$url"
  exit 0
fi

exec powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "$entry_script" "$@"
