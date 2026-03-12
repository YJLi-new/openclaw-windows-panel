#!/usr/bin/env bash
set -euo pipefail

TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$HOME/.openclaw/.gateway-token}"
DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-http://127.0.0.1:18789/}"

if [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]]; then
  TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
else
  TOKEN="dev-local-token"
fi

URL="${DASHBOARD_URL_BASE}#token=${TOKEN}"

if command -v wslview >/dev/null 2>&1; then
  wslview "$URL" >/dev/null 2>&1 &
elif command -v explorer.exe >/dev/null 2>&1; then
  explorer.exe "$URL" >/dev/null 2>&1 &
else
  printf '%s\n' "$URL"
fi
