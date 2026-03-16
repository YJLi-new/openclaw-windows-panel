#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_BRIDGE="${SCRIPT_DIR}/openclaw-wsl-admin-bridge.sh"
if [[ -f "$ADMIN_BRIDGE" ]]; then
  exec "$ADMIN_BRIDGE" dashboard
fi

TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$HOME/.openclaw/.gateway-token}"
DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-http://127.0.0.1:18789/}"

if [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]]; then
  TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
else
  TOKEN="dev-local-token"
fi

URL="${DASHBOARD_URL_BASE}#token=${TOKEN}"

if command -v powershell.exe >/dev/null 2>&1; then
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Start-Process '$URL'" >/dev/null 2>&1 || true
elif command -v explorer.exe >/dev/null 2>&1; then
  explorer.exe "$URL" >/dev/null 2>&1 || true
elif command -v wslview >/dev/null 2>&1; then
  wslview "$URL" >/dev/null 2>&1 || true
fi

printf '%s\n' "$URL"
