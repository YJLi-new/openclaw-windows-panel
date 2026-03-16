#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ADMIN_BRIDGE="${SCRIPT_DIR}/openclaw-wsl-admin-bridge.sh"
if [[ -f "$ADMIN_BRIDGE" ]]; then
  exec "$ADMIN_BRIDGE" "$@"
fi

unset HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY
unset http_proxy https_proxy all_proxy no_proxy

SERVICE_NAME="${OPENCLAW_WSL_NATIVE_SERVICE:-openclaw-gateway.service}"
TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$HOME/.openclaw/.gateway-token}"
DATA_DIR="${OPENCLAW_DATA_DIR:-$HOME/.openclaw}"
CONFIG_PATH="${OPENCLAW_CONFIG_PATH:-$DATA_DIR/openclaw.json}"
SESSIONS_PATH="${OPENCLAW_MAIN_SESSION_STORE:-$DATA_DIR/agents/main/sessions/sessions.json}"
SENTINEL_PID_FILE="${OPENCLAW_WSL_NATIVE_SENTINEL_PID_FILE:-$HOME/.openclaw/.wsl-native-gateway-sentinel.pid}"
SENTINEL_NAME="${OPENCLAW_WSL_NATIVE_SENTINEL_NAME:-openclaw gateway sentinel}"
DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-http://127.0.0.1:18790/}"
HEALTH_URL="${OPENCLAW_WSL_NATIVE_HEALTH_URL:-${DASHBOARD_URL_BASE%/}/health}"
OPENCLAW_BIN="${OPENCLAW_WSL_OPENCLAW_PATH:-/usr/local/bin/openclaw}"

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

get_dashboard_url() {
  local extra_args=()
  local output
  while (($#)); do
    if [[ "$1" != "--no-open" ]]; then
      extra_args+=("$1")
    fi
    shift
  done
  output="$("$OPENCLAW_BIN" dashboard --no-open "${extra_args[@]}" 2>/dev/null || true)"
  extract_dashboard_url "$output" || true
}

ensure_parent_dir() {
  mkdir -p "$(dirname "$1")"
}

service_exec_start() {
  local exec_line
  exec_line="$(systemctl --user show "$SERVICE_NAME" --property ExecStart --value 2>/dev/null || true)"
  exec_line="${exec_line//$'\n'/ }"
  printf '%s' "$exec_line"
}

resolve_project_dir() {
  local exec_line candidate

  if [[ -n "${OPENCLAW_WSL_PROJECT_DIR:-}" && -d "${OPENCLAW_WSL_PROJECT_DIR}" ]]; then
    printf '%s' "${OPENCLAW_WSL_PROJECT_DIR}"
    return 0
  fi

  exec_line="$(service_exec_start)"
  if [[ "$exec_line" =~ (/[^[:space:];]*)/dist/index\.js ]]; then
    candidate="${BASH_REMATCH[1]}"
    if [[ -d "$candidate" ]]; then
      printf '%s' "$candidate"
      return 0
    fi
  fi

  for candidate in \
    "$HOME/src/openclaw-cn" \
    "$HOME/src/openclaw" \
    "$HOME/openclaw-cn" \
    "$HOME/openclaw"
  do
    if [[ -d "$candidate" ]]; then
      printf '%s' "$candidate"
      return 0
    fi
  done

  printf ''
}

print_runtime_diagnostics() {
  local project_dir exec_line
  project_dir="$(resolve_project_dir)"
  exec_line="$(service_exec_start)"

  echo 'runtime_host=wsl2'
  echo "runtime_service=${SERVICE_NAME}"
  echo "runtime_user=$(whoami)"
  echo "runtime_distro=${WSL_DISTRO_NAME:-unknown}"
  if [[ -n "$project_dir" ]]; then
    echo "runtime_project_dir=${project_dir}"
  else
    echo 'runtime_project_dir=unknown'
  fi
  echo "runtime_data_dir=${DATA_DIR}"
  echo "runtime_config_path=${CONFIG_PATH}"
  if [[ -f "$CONFIG_PATH" ]]; then
    echo 'runtime_config_exists=yes'
  else
    echo 'runtime_config_exists=no'
  fi
  echo "runtime_sessions_path=${SESSIONS_PATH}"
  if [[ -f "$SESSIONS_PATH" ]]; then
    echo 'runtime_sessions_exists=yes'
  else
    echo 'runtime_sessions_exists=no'
  fi
  if [[ -n "$exec_line" ]]; then
    echo "runtime_exec_start=${exec_line}"
  fi
  echo 'runtime_diagnose_hint=openclaw-find-runtime-paths.ps1'
}

get_token() {
  local url
  url="$(get_dashboard_url)"
  if [[ "$url" =~ [?#]token=([^&]+) ]]; then
    printf '%s' "${BASH_REMATCH[1]}"
  elif [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]]; then
    tr -d '\r\n' < "$TOKEN_FILE"
  else
    printf ''
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
  print_runtime_diagnostics
}

open_dashboard() {
  local no_open url
  no_open=0
  for arg in "$@"; do
    if [[ "$arg" == "--no-open" ]]; then
      no_open=1
      break
    fi
  done
  url="$(get_dashboard_url "$@")"
  if [[ -z "$url" ]]; then
    url="$(resolve_control_ui_url)"
  fi

  if [[ "$no_open" -eq 0 ]]; then
    if ! open_windows_url "$url"; then
      if command -v wslview >/dev/null 2>&1; then
        wslview "$url" >/dev/null 2>&1 || true
      fi
    fi
  fi

  printf 'dashboard_url=%s\n' "$url"
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
      echo "Dashboard: $(get_dashboard_url --no-open || resolve_control_ui_url)"
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
    open_dashboard "$@"
    exit 0
  fi

  if [[ "${1:-}" == "runtime-paths" || "${1:-}" == "where" || "${1:-}" == "doctor.runtime-paths" ]]; then
    print_runtime_diagnostics
    exit 0
  fi

  status_block "${1:-status}"
}

main "$@"
