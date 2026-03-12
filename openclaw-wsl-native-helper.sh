#!/usr/bin/env bash
set -euo pipefail

unset HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY
unset http_proxy https_proxy all_proxy no_proxy

SERVICE_NAME="${OPENCLAW_WSL_NATIVE_SERVICE:-openclaw-gateway.service}"
TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$HOME/.openclaw/.gateway-token}"
SENTINEL_PID_FILE="${OPENCLAW_WSL_NATIVE_SENTINEL_PID_FILE:-$HOME/.openclaw/.wsl-native-gateway-sentinel.pid}"
SENTINEL_NAME="${OPENCLAW_WSL_NATIVE_SENTINEL_NAME:-openclaw gateway sentinel}"
DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-http://127.0.0.1:18789/}"
HEALTH_URL="${OPENCLAW_WSL_NATIVE_HEALTH_URL:-${DASHBOARD_URL_BASE%/}/health}"

ensure_parent_dir() {
  mkdir -p "$(dirname "$1")"
}

get_token() {
  if [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]]; then
    tr -d '\r\n' < "$TOKEN_FILE"
  else
    printf 'dev-local-token'
  fi
}

service_running() {
  systemctl --user is-active "$SERVICE_NAME" >/dev/null 2>&1
}

stop_sentinel() {
  if [[ -f "$SENTINEL_PID_FILE" ]]; then
    kill "$(cat "$SENTINEL_PID_FILE")" >/dev/null 2>&1 || true
    rm -f "$SENTINEL_PID_FILE"
  fi
  pkill -f "$SENTINEL_NAME" >/dev/null 2>&1 || true
}

ensure_sentinel() {
  if service_running; then
    if ! pgrep -f "$SENTINEL_NAME" >/dev/null 2>&1; then
      ensure_parent_dir "$SENTINEL_PID_FILE"
      nohup bash -lc "exec -a \"$SENTINEL_NAME\" sleep infinity" >/tmp/openclaw-wsl-native-sentinel.log 2>&1 &
      echo $! > "$SENTINEL_PID_FILE"
    fi
    return 0
  fi
  stop_sentinel
}

probe_http_code() {
  local url="$1"
  if ! command -v curl >/dev/null 2>&1; then
    printf '000'
    return 0
  fi
  local code
  code="$(curl --noproxy '*' -m 4 -s -o /dev/null -w '%{http_code}' "$url" 2>/dev/null || true)"
  if [[ -z "$code" ]]; then
    printf '000'
  else
    printf '%s' "$code"
  fi
}

status_block() {
  local token root_code health_code action_name
  action_name="${1:-status}"
  token="$(get_token)"
  root_code="$(probe_http_code "$DASHBOARD_URL_BASE")"
  health_code="$(probe_http_code "$HEALTH_URL")"

  if [[ "$root_code" == "000" && "$health_code" == "200" ]]; then
    root_code="200"
  fi

  ensure_sentinel

  echo "mode=wsl_native"
  echo "action=${action_name}"
  echo "time=$(date '+%F %T')"
  echo "docker=n/a"
  if [[ -n "$token" ]]; then
    echo "token=ok"
  else
    echo "token=missing"
  fi
  if service_running; then
    echo "gateway=running"
    echo "gateway_container=running"
  else
    echo "gateway=stopped"
    echo "gateway_container=stopped"
  fi
  echo "http_root=${root_code}"
  echo "http_health=${health_code}"
  echo "ok=1"
}

open_dashboard() {
  local token url
  token="$(get_token)"
  url="${DASHBOARD_URL_BASE}#token=${token}"

  if command -v wslview >/dev/null 2>&1; then
    wslview "$url" >/dev/null 2>&1 &
  elif command -v explorer.exe >/dev/null 2>&1; then
    explorer.exe "$url" >/dev/null 2>&1 &
  else
    printf '%s\n' "$url"
  fi
}

main() {
  if [[ "${1:-}" == "config" && "${2:-}" == "get" && "${3:-}" == "gateway.auth.token" ]]; then
    ensure_sentinel
    get_token
    exit 0
  fi

  if [[ "${1:-}" == "gateway" && "${2:-}" == "status" ]]; then
    ensure_sentinel
    if service_running; then
      echo "Runtime: running"
      echo "Dashboard: ${DASHBOARD_URL_BASE}"
    else
      echo "Runtime: stopped"
    fi
    exit 0
  fi

  if [[ "${1:-}" == "gateway" && "${2:-}" == "start" ]]; then
    systemctl --user restart "$SERVICE_NAME"
    sleep 1
    ensure_sentinel
    service_running
    exit $?
  fi

  if [[ "${1:-}" == "gateway" && "${2:-}" == "stop" ]]; then
    systemctl --user stop "$SERVICE_NAME" >/dev/null 2>&1 || true
    ensure_sentinel
    exit 0
  fi

  if [[ "${1:-}" == "gateway" && "${2:-}" == "run" ]]; then
    systemctl --user restart "$SERVICE_NAME"
    sleep 1
    ensure_sentinel
    service_running
    exit $?
  fi

  if [[ "${1:-}" == "dashboard" ]]; then
    open_dashboard
    exit 0
  fi

  status_block "${1:-status}"
}

main "$@"
