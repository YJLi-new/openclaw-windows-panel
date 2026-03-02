# OpenClaw Control Panel - Release (Privacy-safe)

This folder contains a privacy-safe release package.

## Included files

- `openclaw-control-panel.exe` (single effective build output)
- `openclaw-control-panel.ico`
- `openclaw-start-fast.sh`
- `openclaw-open-dashboard-wsl.sh`
- `openclaw-docker-dashboard.sh`
- `openclaw-approve-pairing.sh`
- `openclaw-control-panel-settings.template.json`

## Not included (privacy-sensitive/runtime files)

- your actual settings file (`openclaw-control-panel-settings.json`)
- logs (`openclaw-control-panel-error.log`, launch logs)
- tokens/cookies (`.gateway-token`, any auth cookies)
- any password file (e.g. `.sudo-pass`)

## Usage

1. Run `openclaw-control-panel.exe`.
2. Open `Settings` and configure all paths/mode/network values for your environment.
3. If WSL requires `sudo` password for Docker bootstrap, set it in `Settings > Network` (stored with DPAPI encryption).
4. Optional: copy `openclaw-control-panel-settings.template.json` to runtime settings path, then fill every field manually.

## Cleared Config Policy

- This release intentionally clears all user-configurable values in templates.
- Release scripts no longer include hardcoded local paths/URLs/ports.
- Before running release scripts manually, set environment variables such as:
  - Common path vars: `OPENCLAW_BASE_DIR`, `OPENCLAW_ROOT_DIR`, `OPENCLAW_DATA_DIR`
  - Gateway vars: `OPENCLAW_GATEWAY_ROOT_URL`, `OPENCLAW_GATEWAY_PORT`, `OPENCLAW_BRIDGE_PORT`, `OPENCLAW_GATEWAY_BIND`
  - Script endpoint vars: `OPENCLAW_DASHBOARD_URL_BASE`, `OPENCLAW_WS_URL`
  - Image var: `OPENCLAW_IMAGE`

## Notes

- From now on, build only one EXE target: `openclaw-control-panel.exe`.
- This release package is path/config neutral and does not contain personal credentials.
- `openclaw-start-fast.sh` in this release does not read any local password file.
