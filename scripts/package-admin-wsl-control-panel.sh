#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
DIST_ROOT="$ROOT_DIR/dist"
PACKAGE_DIR="$DIST_ROOT/openclaw-control-panel-admin-wsl"
ZIP_PATH="$DIST_ROOT/openclaw-control-panel-admin-wsl.zip"

mkdir -p "$DIST_ROOT"
rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR"

files=(
  "openclaw-control-panel.exe"
  "assets/openclaw-control-panel.ico"
  "scripts/openclaw-find-runtime-paths.ps1"
  "scripts/openclaw-open-dashboard-wsl.sh"
  "scripts/openclaw-start-fast.sh"
  "scripts/openclaw-wsl-bridge-runner.ps1"
  "scripts/openclaw-wsl-bridge.ps1"
  "scripts/openclaw-wsl-admin-bridge.sh"
  "scripts/openclaw-wsl-native-dashboard.sh"
  "scripts/openclaw-wsl-native-helper.sh"
  "scripts/install-openclaw-wsl-bridge.ps1"
  "scripts/openclaw-win-admin-wsl-gateway-task.ps1"
  "scripts/openclaw-win-admin-wsl-entry.ps1"
  "scripts/openclaw-win-admin-wsl-install.ps1"
  "scripts/new-admin-wsl-panel-settings.ps1"
  "docs/ADMIN_WSL_BUNDLE.md"
)

for file in "${files[@]}"; do
  cp "$ROOT_DIR/$file" "$PACKAGE_DIR/$(basename "$file")"
done

(
  cd "$PACKAGE_DIR"
  for file in "${files[@]}"; do
    sha256sum "$(basename "$file")"
  done > SHA256SUMS.txt
)

rm -f "$ZIP_PATH"
python3 - <<'PY' "$DIST_ROOT" "$(basename "$PACKAGE_DIR")" "$ZIP_PATH"
import pathlib
import sys
import zipfile

dist_root = pathlib.Path(sys.argv[1])
package_name = sys.argv[2]
zip_path = pathlib.Path(sys.argv[3])
package_dir = dist_root / package_name

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as zf:
    for path in sorted(package_dir.rglob("*")):
        if path.is_file():
            zf.write(path, path.relative_to(dist_root))
PY

echo "Package directory: $PACKAGE_DIR"
echo "Package zip:       $ZIP_PATH"
