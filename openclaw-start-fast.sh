#!/usr/bin/env bash
set -euo pipefail

BASE_DIR="${OPENCLAW_BASE_DIR:-}"
if [[ -z "$BASE_DIR" ]]; then
  echo "Please set OPENCLAW_BASE_DIR first." >&2
  exit 2
fi
ROOT_DIR="${OPENCLAW_ROOT_DIR:-$BASE_DIR/openclaw}"
TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$BASE_DIR/openclaw-data/.gateway-token}"
ENV_FILE="${OPENCLAW_ENV_FILE:-$ROOT_DIR/.env}"
COMPOSE_FILE="${OPENCLAW_COMPOSE_FILE:-$ROOT_DIR/docker-compose.yml}"
FULL_BOOTSTRAP_SCRIPT="${OPENCLAW_BOOTSTRAP_SCRIPT:-$BASE_DIR/openclaw-docker-dashboard.sh}"
OPENCLAW_IMAGE="${OPENCLAW_IMAGE:-}"
OPENCLAW_GATEWAY_ROOT_URL="${OPENCLAW_GATEWAY_ROOT_URL:-}"
if [[ -z "$OPENCLAW_IMAGE" ]]; then
  echo "Please set OPENCLAW_IMAGE first." >&2
  exit 2
fi
if [[ -z "$OPENCLAW_GATEWAY_ROOT_URL" ]]; then
  echo "Please set OPENCLAW_GATEWAY_ROOT_URL first." >&2
  exit 2
fi

log() {
  printf '[fast-start] %s\n' "$*"
}

run_as_root() {
  local cmd="$1"
  if command -v sudo >/dev/null 2>&1; then
    if sudo -n true >/dev/null 2>&1; then
      sudo -n bash -lc "$cmd"
      return $?
    fi
    if [[ -n "${OPENCLAW_PANEL_SUDO_PASS:-}" ]]; then
      printf '%s\n' "$OPENCLAW_PANEL_SUDO_PASS" | sudo -S -p '' bash -lc "$cmd"
      return $?
    fi
    if [[ -n "${OPENCLAW_SUDO_PASSWORD:-}" ]]; then
      printf '%s\n' "$OPENCLAW_SUDO_PASSWORD" | sudo -S -p '' bash -lc "$cmd"
      return $?
    fi
  fi
  return 1
}

wait_docker_ready() {
  local tries="$1"
  for _ in $(seq 1 "$tries"); do
    if docker info >/dev/null 2>&1; then
      return 0
    fi
    sleep 2
  done
  return 1
}

restart_dockerd_clean() {
  run_as_root 'pkill -x dockerd >/dev/null 2>&1 || true; pkill -x containerd >/dev/null 2>&1 || true; rm -f /var/run/docker.sock /var/run/docker.pid' || true
  sleep 1
  run_as_root 'nohup dockerd > /tmp/dockerd.log 2>&1 &' || true
}

ensure_docker_reachable() {
  if docker info >/dev/null 2>&1; then
    log "Docker daemon already reachable."
    return 0
  fi

  log "Docker daemon unreachable in WSL. Trying to start dockerd..."

  if pgrep -x dockerd >/dev/null 2>&1; then
    log "dockerd process exists but daemon is unreachable; restarting dockerd."
    restart_dockerd_clean
    log "dockerd restart attempted."
  elif command -v dockerd >/dev/null 2>&1; then
    run_as_root 'nohup dockerd > /tmp/dockerd.log 2>&1 &' >/dev/null 2>&1 || true
    log "Started dockerd."
  else
    log "dockerd binary not found in WSL."
  fi

  if wait_docker_ready 7; then
    log "Docker daemon became reachable."
    return 0
  fi

  log "First start attempt did not become ready; retrying with clean restart."
  restart_dockerd_clean
  if wait_docker_ready 9; then
    log "Docker daemon became reachable on retry."
    return 0
  fi

  log "ERROR: Docker daemon still unreachable."
  if [[ -f /tmp/dockerd.log ]]; then
    log "--- /tmp/dockerd.log (tail) ---"
    tail -n 40 /tmp/dockerd.log || true
  fi
  return 1
}

fast_start_ready() {
  [[ -f "$TOKEN_FILE" && -s "$TOKEN_FILE" ]] || return 1
  [[ -f "$ENV_FILE" ]] || return 1
  [[ -f "$COMPOSE_FILE" ]] || return 1
  docker image inspect "$OPENCLAW_IMAGE" >/dev/null 2>&1 || return 1
  return 0
}

fast_start_gateway() {
  log "Using fast path: docker compose up -d openclaw-gateway"
  cd "$ROOT_DIR"
  if command -v timeout >/dev/null 2>&1; then
    timeout 15 docker compose up -d openclaw-gateway || {
      log "ERROR: docker compose up timed out or failed."
      return 1
    }
  else
    docker compose up -d openclaw-gateway || {
      log "ERROR: docker compose up failed."
      return 1
    }
  fi

  # Wait briefly for gateway HTTP to become reachable so callers can report success confidently.
  for _ in $(seq 1 20); do
    if curl -m 2 -s -o /dev/null "$OPENCLAW_GATEWAY_ROOT_URL"; then
      log "Gateway HTTP is reachable."
      return 0
    fi
    sleep 1
  done
  log "Gateway container started, but HTTP is not ready yet."
}

main() {
  local mode="${1:-start}"
  case "$mode" in
    --ensure-docker-only|ensure-docker-only)
      if ensure_docker_reachable; then
        log "ENSURE_DOCKER_OK"
        exit 0
      fi
      log "ENSURE_DOCKER_FAIL"
      exit 2
      ;;
    --start|start|"")
      ;;
    *)
      log "Unknown mode: $mode"
      log "Supported modes: start, --ensure-docker-only"
      exit 64
      ;;
  esac

  ensure_docker_reachable || exit 2

  if fast_start_ready; then
    if fast_start_gateway; then
      log "FAST_START_OK"
      exit 0
    fi
    log "ERROR: Fast path failed."
    if [[ "${OPENCLAW_ALLOW_FULL_BOOTSTRAP:-0}" == "1" ]]; then
      log "OPENCLAW_ALLOW_FULL_BOOTSTRAP=1 detected; falling back to full bootstrap."
      exec "$FULL_BOOTSTRAP_SCRIPT"
    fi
    log "Run full bootstrap manually once: $FULL_BOOTSTRAP_SCRIPT"
    exit 4
  else
    log "ERROR: Fast path prerequisites missing."
    log "Required: token file, .env, docker-compose.yml, and local image ($OPENCLAW_IMAGE)."
    if [[ "${OPENCLAW_ALLOW_FULL_BOOTSTRAP:-0}" == "1" ]]; then
      log "OPENCLAW_ALLOW_FULL_BOOTSTRAP=1 detected; falling back to full bootstrap."
      exec "$FULL_BOOTSTRAP_SCRIPT"
    fi
    log "Run full bootstrap manually once: $FULL_BOOTSTRAP_SCRIPT"
    exit 3
  fi
}

main "$@"
