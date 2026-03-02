#!/usr/bin/env bash
set -euo pipefail

BASE_DIR="${OPENCLAW_BASE_DIR:-}"
ROOT_DIR="${OPENCLAW_ROOT_DIR:-}"
if [[ -z "$ROOT_DIR" ]]; then
  if [[ -n "$BASE_DIR" ]]; then
    ROOT_DIR="$BASE_DIR/openclaw"
  else
    echo "Please set OPENCLAW_ROOT_DIR or OPENCLAW_BASE_DIR first." >&2
    exit 2
  fi
fi
if [[ -z "$BASE_DIR" ]]; then
  BASE_DIR="$(cd "$ROOT_DIR/.." && pwd)"
fi
DATA_DIR="${OPENCLAW_DATA_DIR:-$BASE_DIR/openclaw-data}"
CONFIG_DIR="${OPENCLAW_CONFIG_DIR:-$DATA_DIR/config}"
WORKSPACE_DIR="${OPENCLAW_WORKSPACE_DIR:-$DATA_DIR/workspace}"
TOKEN_FILE="$DATA_DIR/.gateway-token"
ENV_FILE="$ROOT_DIR/.env"

OPENCLAW_IMAGE="${OPENCLAW_IMAGE:-}"
OPENCLAW_GATEWAY_PORT="${OPENCLAW_GATEWAY_PORT:-}"
OPENCLAW_BRIDGE_PORT="${OPENCLAW_BRIDGE_PORT:-}"
OPENCLAW_GATEWAY_BIND="${OPENCLAW_GATEWAY_BIND:-}"
if [[ -z "$OPENCLAW_IMAGE" || -z "$OPENCLAW_GATEWAY_PORT" || -z "$OPENCLAW_BRIDGE_PORT" || -z "$OPENCLAW_GATEWAY_BIND" ]]; then
  echo "Please set OPENCLAW_IMAGE, OPENCLAW_GATEWAY_PORT, OPENCLAW_BRIDGE_PORT, and OPENCLAW_GATEWAY_BIND first." >&2
  exit 2
fi

require_cmd() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Missing dependency: $1" >&2
    exit 1
  fi
}

is_wsl_env() {
  [[ -n "${WSL_DISTRO_NAME:-}" ]] && return 0
  [[ -r /proc/version ]] && grep -qi "microsoft" /proc/version && return 0
  return 1
}

configure_wsl_clash_proxy() {
  # Set OPENCLAW_AUTO_WSL_PROXY=0 to disable auto-proxy bootstrap.
  if [[ "${OPENCLAW_AUTO_WSL_PROXY:-1}" == "0" ]]; then
    return 0
  fi

  # Respect explicit caller-provided proxy values.
  if [[ -n "${HTTP_PROXY:-}" || -n "${HTTPS_PROXY:-}" || -n "${ALL_PROXY:-}" ]]; then
    return 0
  fi

  if ! is_wsl_env; then
    return 0
  fi

  local wsl_host_ip
  wsl_host_ip="$(ip route 2>/dev/null | awk '/default/ {print $3; exit}')"
  if [[ -z "$wsl_host_ip" ]]; then
    return 0
  fi

  export HTTPS_PROXY="http://${wsl_host_ip}:4780"
  export HTTP_PROXY="$HTTPS_PROXY"
  export ALL_PROXY="socks5h://${wsl_host_ip}:4781"
  export NO_PROXY="localhost,127.0.0.1,::1,host.docker.internal,172.17.0.1"
  export https_proxy="$HTTPS_PROXY"
  export http_proxy="$HTTP_PROXY"
  export all_proxy="$ALL_PROXY"
  export no_proxy="$NO_PROXY"

  echo "Using WSL Clash proxy via ${wsl_host_ip} (HTTP 4780 / SOCKS5 4781)."
}

require_cmd docker
if ! docker compose version >/dev/null 2>&1; then
  echo "Docker Compose v2 is required (docker compose version)." >&2
  exit 1
fi

if [[ ! -d "$ROOT_DIR" ]]; then
  echo "OpenClaw repo not found: $ROOT_DIR" >&2
  exit 1
fi

mkdir -p "$DATA_DIR" "$CONFIG_DIR" "$WORKSPACE_DIR" "$CONFIG_DIR/identity"

if [[ -z "${OPENCLAW_GATEWAY_TOKEN:-}" ]]; then
  if [[ -f "$TOKEN_FILE" ]]; then
    OPENCLAW_GATEWAY_TOKEN="$(tr -d '\r\n' <"$TOKEN_FILE")"
  else
    require_cmd openssl
    OPENCLAW_GATEWAY_TOKEN="$(openssl rand -hex 32)"
    printf '%s\n' "$OPENCLAW_GATEWAY_TOKEN" >"$TOKEN_FILE"
    chmod 600 "$TOKEN_FILE" || true
  fi
fi
export OPENCLAW_GATEWAY_TOKEN

configure_wsl_clash_proxy

upsert_env() {
  local file="$1"
  local key="$2"
  local value="$3"
  if [[ -f "$file" ]] && grep -qE "^${key}=" "$file"; then
    sed -i "s|^${key}=.*|${key}=${value}|" "$file"
  else
    printf '%s=%s\n' "$key" "$value" >>"$file"
  fi
}

read_gateway_token_from_config() {
  local config_path="$CONFIG_DIR/openclaw.json"
  if [[ ! -f "$config_path" ]]; then
    return 0
  fi
  if command -v python3 >/dev/null 2>&1; then
    python3 - "$config_path" <<'PY'
import json
import sys

path = sys.argv[1]
try:
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)
except Exception:
    raise SystemExit(0)

token = (((cfg.get("gateway") or {}).get("auth") or {}).get("token"))
if isinstance(token, str):
    token = token.strip()
    if token:
        print(token)
PY
  fi
}

touch "$ENV_FILE"
upsert_env "$ENV_FILE" "OPENCLAW_CONFIG_DIR" "$CONFIG_DIR"
upsert_env "$ENV_FILE" "OPENCLAW_WORKSPACE_DIR" "$WORKSPACE_DIR"
upsert_env "$ENV_FILE" "OPENCLAW_GATEWAY_PORT" "$OPENCLAW_GATEWAY_PORT"
upsert_env "$ENV_FILE" "OPENCLAW_BRIDGE_PORT" "$OPENCLAW_BRIDGE_PORT"
upsert_env "$ENV_FILE" "OPENCLAW_GATEWAY_BIND" "$OPENCLAW_GATEWAY_BIND"
upsert_env "$ENV_FILE" "OPENCLAW_GATEWAY_TOKEN" "$OPENCLAW_GATEWAY_TOKEN"
upsert_env "$ENV_FILE" "OPENCLAW_IMAGE" "$OPENCLAW_IMAGE"
for proxy_var in HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY http_proxy https_proxy all_proxy no_proxy; do
  upsert_env "$ENV_FILE" "$proxy_var" "${!proxy_var:-}"
done

cd "$ROOT_DIR"

if [[ "$OPENCLAW_IMAGE" == "openclaw:local" ]]; then
  BUILD_ARGS=()
  for proxy_var in HTTP_PROXY HTTPS_PROXY ALL_PROXY NO_PROXY http_proxy https_proxy all_proxy no_proxy; do
    proxy_val="${!proxy_var:-}"
    if [[ -n "$proxy_val" ]]; then
      BUILD_ARGS+=(--build-arg "${proxy_var}=${proxy_val}")
    fi
  done
  docker build "${BUILD_ARGS[@]}" -t "$OPENCLAW_IMAGE" -f Dockerfile .
else
  docker pull "$OPENCLAW_IMAGE"
fi

docker compose run --rm openclaw-cli onboard \
  --no-install-daemon \
  --non-interactive \
  --accept-risk \
  --mode local \
  --auth-choice skip \
  --skip-channels \
  --skip-skills \
  --skip-health \
  --skip-ui \
  --gateway-auth token \
  --gateway-token "$OPENCLAW_GATEWAY_TOKEN" \
  --gateway-port "$OPENCLAW_GATEWAY_PORT" \
  --gateway-bind "$OPENCLAW_GATEWAY_BIND"

CONFIG_TOKEN="$(read_gateway_token_from_config || true)"
if [[ -n "$CONFIG_TOKEN" && "$CONFIG_TOKEN" != "$OPENCLAW_GATEWAY_TOKEN" ]]; then
  OPENCLAW_GATEWAY_TOKEN="$CONFIG_TOKEN"
  upsert_env "$ENV_FILE" "OPENCLAW_GATEWAY_TOKEN" "$OPENCLAW_GATEWAY_TOKEN"
  printf '%s\n' "$OPENCLAW_GATEWAY_TOKEN" >"$TOKEN_FILE"
  chmod 600 "$TOKEN_FILE" || true
fi

if [[ "$OPENCLAW_GATEWAY_BIND" != "loopback" ]]; then
  docker compose run --rm openclaw-cli config set gateway.controlUi.allowedOrigins \
    "[\"http://127.0.0.1:${OPENCLAW_GATEWAY_PORT}\",\"http://localhost:${OPENCLAW_GATEWAY_PORT}\"]" \
    --strict-json >/dev/null
fi

# Local dashboard token direct-connect mode (no device pairing prompts).
docker compose run --rm openclaw-cli config set gateway.controlUi.allowInsecureAuth true --strict-json >/dev/null
docker compose run --rm openclaw-cli config set gateway.controlUi.dangerouslyDisableDeviceAuth true --strict-json >/dev/null

docker compose up -d openclaw-gateway

echo "Dashboard: http://127.0.0.1:${OPENCLAW_GATEWAY_PORT}/"
echo "Token: $OPENCLAW_GATEWAY_TOKEN"
echo "Data:  $DATA_DIR"
