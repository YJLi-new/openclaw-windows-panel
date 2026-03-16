#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DIST_ROOT="$ROOT_DIR/dist"
PACKAGE_DIR="$DIST_ROOT/openclaw-control-panel-admin-wsl"
ZIP_PATH="$DIST_ROOT/openclaw-control-panel-admin-wsl.zip"

mkdir -p "$DIST_ROOT"
rm -rf "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR"

files=(
  "openclaw-control-panel.exe"
  "openclaw-control-panel.ico"
  "openclaw-find-runtime-paths.ps1"
  "openclaw-wsl-bridge-runner.ps1"
  "openclaw-wsl-bridge.ps1"
  "install-openclaw-wsl-bridge.ps1"
  "openclaw-win-admin-wsl-gateway-task.ps1"
  "openclaw-win-admin-wsl-entry.ps1"
  "openclaw-win-admin-wsl-install.ps1"
  "new-admin-wsl-panel-settings.ps1"
  "ADMIN_WSL_BUNDLE.md"
)

for file in "${files[@]}"; do
  cp "$ROOT_DIR/$file" "$PACKAGE_DIR/$file"
done

(
  cd "$PACKAGE_DIR"
  sha256sum "${files[@]}" > SHA256SUMS.txt
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
