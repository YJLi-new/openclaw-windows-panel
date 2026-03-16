#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_BRIDGE="${SCRIPT_DIR}/openclaw-wsl-admin-bridge.sh"
if [[ -f "$ADMIN_BRIDGE" ]]; then
  exec "$ADMIN_BRIDGE" dashboard
fi

TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$HOME/.openclaw/.gateway-token}"
OPENCLAW_BIN="${OPENCLAW_WSL_OPENCLAW_PATH:-/usr/local/bin/openclaw}"
DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-http://127.0.0.1:18790/}"

resolve_control_ui_url() {
  local raw="${OPENCLAW_CONTROL_UI_URL_BASE:-$DASHBOARD_URL_BASE}"
  printf '%s' "$raw"
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

extract_dashboard_url() {
  local output="$1"
  local line trimmed
  while IFS= read -r line; do
    trimmed="${line//$'\r'/}"
    if [[ "$trimmed" =~ ^Dashboard\ URL:\ ([^[:space:]]+)$ ]]; then
      printf '%s' "${BASH_REMATCH[1]}"
      return 0
    fi
    if [[ "$trimmed" =~ ^dashboard_url=([^[:space:]]+)$ ]]; then
      printf '%s' "${BASH_REMATCH[1]}"
      return 0
    fi
    if [[ "$trimmed" =~ ^https?://[^[:space:]]+$ ]]; then
      printf '%s' "$trimmed"
      return 0
    fi
  done <<< "$output"
  return 1
}

no_open=0
for arg in "$@"; do
  if [[ "$arg" == "--no-open" ]]; then
    no_open=1
    break
  fi
done

OUTPUT="$("$OPENCLAW_BIN" dashboard --no-open 2>/dev/null || true)"
URL="$(extract_dashboard_url "$OUTPUT" || true)"
if [[ -z "$URL" ]]; then
  if [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]]; then
    TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
  else
    TOKEN=""
  fi
  URL="$(resolve_control_ui_url)"
  if [[ -n "$TOKEN" ]]; then
    URL="${URL}#token=${TOKEN}"
  fi
fi

if [[ "$no_open" -eq 0 ]]; then
  if ! open_windows_url "$URL"; then
    if command -v wslview >/dev/null 2>&1; then
      wslview "$URL" >/dev/null 2>&1 || true
    fi
  fi
fi

printf 'dashboard_url=%s\n' "$URL"
