#!/usr/bin/env bash
set -euo pipefail

BASE_DIR="${OPENCLAW_BASE_DIR:-}"
if [ -z "$BASE_DIR" ]; then
  echo "Please set OPENCLAW_BASE_DIR first." >&2
  exit 2
fi
OPENCLAW_DIR="${OPENCLAW_ROOT_DIR:-$BASE_DIR/openclaw}"
TOKEN_FILE="${OPENCLAW_TOKEN_FILE:-$BASE_DIR/openclaw-data/.gateway-token}"
OPENCLAW_WS_URL="${OPENCLAW_WS_URL:-}"
if [ -z "$OPENCLAW_WS_URL" ]; then
  echo "Please set OPENCLAW_WS_URL first." >&2
  exit 2
fi

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

if [ ! -d "$OPENCLAW_DIR" ]; then
  echo "OpenClaw directory not found: $OPENCLAW_DIR" >&2
  exit 1
fi

if docker info >/dev/null 2>&1; then
  DC=(docker compose)
else
  DC=(sudo docker compose)
fi

list_devices_json() {
  local raw
  raw="$(
    cd "$OPENCLAW_DIR"
    "${DC[@]}" exec -T openclaw-gateway \
      node dist/index.js devices list \
      --url "$OPENCLAW_WS_URL" \
      --token "$TOKEN" \
      --json
  )"
  printf "%s\n" "$raw" | awk 'BEGIN{p=0} /^[[:space:]]*{/{p=1} p{print}'
}

approve_one() {
  local request_id="$1"
  cd "$OPENCLAW_DIR"
  "${DC[@]}" exec -T openclaw-gateway \
    node dist/index.js devices approve "$request_id" \
    --url "$OPENCLAW_WS_URL" \
    --token "$TOKEN"
}

extract_pending_request_ids() {
  node -e '
const fs = require("fs");
const raw = fs.readFileSync(0, "utf8");
const json = JSON.parse(raw);
for (const req of (json.pending || [])) {
  if (req && typeof req.requestId === "string" && req.requestId.trim()) {
    process.stdout.write(req.requestId.trim() + "\n");
  }
}
'
}

count_pending() {
  node -e '
const fs = require("fs");
const raw = fs.readFileSync(0, "utf8");
const json = JSON.parse(raw);
process.stdout.write(String((json.pending || []).length));
'
}

JSON_NOW="$(list_devices_json)"
PENDING_IDS="$(printf "%s" "$JSON_NOW" | extract_pending_request_ids || true)"

if [ -z "$PENDING_IDS" ]; then
  echo "No pending pairing requests."
  exit 0
fi

APPROVED=0
while IFS= read -r request_id; do
  [ -n "$request_id" ] || continue
  approve_one "$request_id"
  APPROVED=$((APPROVED + 1))
done <<< "$PENDING_IDS"

JSON_AFTER="$(list_devices_json)"
PENDING_LEFT="$(printf "%s" "$JSON_AFTER" | count_pending)"

echo "Approved requests: $APPROVED"
echo "Pending left: $PENDING_LEFT"
