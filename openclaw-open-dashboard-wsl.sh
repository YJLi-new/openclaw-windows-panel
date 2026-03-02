#!/usr/bin/env bash
set -euo pipefail

BASE_DIR="${OPENCLAW_BASE_DIR:-}"
if [ -z "$BASE_DIR" ]; then
  echo "Please set OPENCLAW_BASE_DIR first." >&2
  exit 2
fi
TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$BASE_DIR/openclaw-data/.gateway-token}"
PROFILE_DIR="${OPENCLAW_CHROME_PROFILE_DIR:-$BASE_DIR/openclaw-data/chrome-profile}"
LOG_FILE="/tmp/openclaw-chrome.log"
FCITX_PROFILE="$HOME/.config/fcitx5/profile"
OPENCLAW_CHROME_CDP_PORT="${OPENCLAW_CHROME_CDP_PORT:-19222}"
OPENCLAW_CHROME_CDP_BRIDGE_PORT="${OPENCLAW_CHROME_CDP_BRIDGE_PORT:-29222}"
CDP_BRIDGE_LOG_FILE="/tmp/openclaw-cdp-bridge.log"

if [ ! -f "$TOKEN_FILE" ]; then
  echo "Token file not found: $TOKEN_FILE" >&2
  echo "Start OpenClaw first, then try again." >&2
  exit 1
fi

TOKEN="$(tr -d '\r\n' < "$TOKEN_FILE")"
if [ -z "$TOKEN" ]; then
  echo "Dashboard token is empty in $TOKEN_FILE" >&2
  exit 1
fi

OPENCLAW_DASHBOARD_URL_BASE="${OPENCLAW_DASHBOARD_URL_BASE:-}"
if [ -z "$OPENCLAW_DASHBOARD_URL_BASE" ]; then
  echo "Please set OPENCLAW_DASHBOARD_URL_BASE first." >&2
  exit 2
fi
URL="${OPENCLAW_DASHBOARD_URL_BASE}#token=$TOKEN"

if command -v google-chrome >/dev/null 2>&1; then
  CHROME_BIN="$(command -v google-chrome)"
elif command -v google-chrome-stable >/dev/null 2>&1; then
  CHROME_BIN="$(command -v google-chrome-stable)"
else
  echo "Linux Chrome not found. Install google-chrome first." >&2
  exit 1
fi

mkdir -p "$PROFILE_DIR"
mkdir -p "$(dirname "$FCITX_PROFILE")"

if [ ! -f "$FCITX_PROFILE" ] || ! grep -q '^Name=pinyin$' "$FCITX_PROFILE"; then
  cat >"$FCITX_PROFILE" <<'EOF'
[Groups/0]
Name=Default
Default Layout=us
DefaultIM=keyboard-us

[Groups/0/Items/0]
Name=keyboard-us
Layout=

[Groups/0/Items/1]
Name=pinyin
Layout=

[GroupOrder]
0=Default
EOF
  chmod 600 "$FCITX_PROFILE" || true
fi

LAUNCHER_SCRIPT="/tmp/openclaw-chrome-ime-launcher.sh"
cat >"$LAUNCHER_SCRIPT" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

export GTK_IM_MODULE=fcitx
export QT_IM_MODULE=fcitx
export XMODIFIERS='@im=fcitx'
export SDL_IM_MODULE=fcitx

# WSLg + fcitx5 is more stable on X11 backend.
export WAYLAND_DISPLAY=""

if command -v fcitx5 >/dev/null 2>&1; then
  pkill -x fcitx5 >/dev/null 2>&1 || true
  fcitx5 --disable=wayland,waylandim -d >/dev/null 2>&1 || true
fi

exec "$@"
EOF
chmod +x "$LAUNCHER_SCRIPT"

CHROME_ARGS=(
  --user-data-dir="$PROFILE_DIR"
  --no-first-run
  --no-default-browser-check
  --disable-dev-shm-usage
  --password-store=basic
  --ozone-platform=x11
  --remote-debugging-port="$OPENCLAW_CHROME_CDP_PORT"
  --remote-debugging-address=0.0.0.0
  --remote-allow-origins=*
  "$URL"
)

# Relaunch the profile cleanly so new flags (e.g. CDP port) always take effect.
mapfile -t OLD_CHROME_PIDS < <(pgrep -f -- "--user-data-dir=$PROFILE_DIR" || true)
if [ "${#OLD_CHROME_PIDS[@]}" -gt 0 ]; then
  kill "${OLD_CHROME_PIDS[@]}" >/dev/null 2>&1 || true
  sleep 1
fi
rm -f "$PROFILE_DIR"/SingletonCookie "$PROFILE_DIR"/SingletonLock "$PROFILE_DIR"/SingletonSocket || true

if [ -z "${DBUS_SESSION_BUS_ADDRESS:-}" ] && command -v dbus-run-session >/dev/null 2>&1; then
  nohup dbus-run-session -- "$LAUNCHER_SCRIPT" "$CHROME_BIN" "${CHROME_ARGS[@]}" >"$LOG_FILE" 2>&1 &
  LAUNCH_MODE="dbus-run-session"
else
  nohup "$LAUNCHER_SCRIPT" "$CHROME_BIN" "${CHROME_ARGS[@]}" >"$LOG_FILE" 2>&1 &
  LAUNCH_MODE="current-session"
fi

PID=$!

if command -v socat >/dev/null 2>&1; then
  pkill -f "socat TCP-LISTEN:${OPENCLAW_CHROME_CDP_BRIDGE_PORT},fork,reuseaddr,bind=0.0.0.0 TCP:127.0.0.1:${OPENCLAW_CHROME_CDP_PORT}" >/dev/null 2>&1 || true
  nohup socat \
    "TCP-LISTEN:${OPENCLAW_CHROME_CDP_BRIDGE_PORT},fork,reuseaddr,bind=0.0.0.0" \
    "TCP:127.0.0.1:${OPENCLAW_CHROME_CDP_PORT}" >"$CDP_BRIDGE_LOG_FILE" 2>&1 &
  CDP_BRIDGE_PID=$!
else
  CDP_BRIDGE_PID=""
fi

echo "Opened OpenClaw chat in Linux Chrome."
echo "PID: $PID"
echo "URL: $URL"
echo "Profile: $PROFILE_DIR"
echo "Log: $LOG_FILE"
echo "Launch mode: $LAUNCH_MODE"
echo "IME: fcitx5 (Ctrl+Space to toggle EN/中文)"
echo "CDP: http://127.0.0.1:$OPENCLAW_CHROME_CDP_PORT"
if [ -n "$CDP_BRIDGE_PID" ]; then
  echo "CDP Bridge PID: $CDP_BRIDGE_PID"
  echo "CDP Bridge: http://127.0.0.1:$OPENCLAW_CHROME_CDP_BRIDGE_PORT -> 127.0.0.1:$OPENCLAW_CHROME_CDP_PORT"
  echo "CDP Bridge Log: $CDP_BRIDGE_LOG_FILE"
fi
