#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_BRIDGE="${SCRIPT_DIR}/openclaw-wsl-admin-bridge.sh"
if [[ -f "$ADMIN_BRIDGE" ]]; then
  exec "$ADMIN_BRIDGE" dashboard
fi

TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$HOME/.openclaw/.gateway-token}"
DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-http://127.0.0.1:18789/}"

resolve_control_ui_url() {
  local raw="${OPENCLAW_CONTROL_UI_URL_BASE:-$DASHBOARD_URL_BASE}"
  if [[ "$raw" == *"/chat?session=main" ]]; then
    printf '%s' "$raw"
    return 0
  fi
  if [[ "$raw" =~ ^(https?://[^/:]+):18790/?$ ]]; then
    printf '%s' "${BASH_REMATCH[1]}:18789/chat?session=main"
    return 0
  fi
  printf '%s' "${raw%/}/chat?session=main"
}

open_windows_url() {
  local url="$1"
  if command -v powershell.exe >/dev/null 2>&1; then
    OPENCLAW_TARGET_URL="$url" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "[System.Diagnostics.Process]::Start($env:OPENCLAW_TARGET_URL) | Out-Null" >/dev/null 2>&1 && return 0
    OPENCLAW_TARGET_URL="$url" powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath 'rundll32.exe' -ArgumentList 'url.dll,FileProtocolHandler', $env:OPENCLAW_TARGET_URL" >/dev/null 2>&1 && return 0
  fi
  if command -v rundll32.exe >/dev/null 2>&1; then
    rundll32.exe url.dll,FileProtocolHandler "$url" >/dev/null 2>&1 && return 0
  fi
  if command -v explorer.exe >/dev/null 2>&1; then
    explorer.exe "$url" >/dev/null 2>&1 && return 0
  fi
  return 1
}

if [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]]; then
  TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
else
  TOKEN="dev-local-token"
fi

URL="$(resolve_control_ui_url)#token=${TOKEN}"

if ! open_windows_url "$URL"; then
  if command -v wslview >/dev/null 2>&1; then
  wslview "$URL" >/dev/null 2>&1 || true
  fi
fi

printf '%s\n' "$URL"
