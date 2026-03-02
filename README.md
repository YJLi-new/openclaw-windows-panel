# OpenClaw Windows Control Panel (Release)

A lightweight Windows GUI to start, stop, and monitor OpenClaw across multiple deployment modes.

## Supported Deployment Modes

- `wsl_docker`: OpenClaw runs in Docker inside WSL.
- `wsl_native`: OpenClaw runs directly in WSL (no Docker).
- `win_docker`: OpenClaw runs in Docker on Windows.
- `win_native`: OpenClaw runs directly on Windows.

## Privacy-Safe Release Notes

- This release **does not include** personal runtime files.
- All configurable fields are intentionally blank in the template.
- No tokens, cookies, logs, or plaintext passwords are bundled.

## Included Files

- `openclaw-control-panel.exe`
- `openclaw-control-panel.ico`
- `openclaw-start-fast.sh`
- `openclaw-open-dashboard-wsl.sh`
- `openclaw-docker-dashboard.sh`
- `openclaw-approve-pairing.sh`
- `openclaw-control-panel-settings.template.json`
- `SHA256SUMS.txt`

## Quick Start

1. Keep all files in the same folder.
2. Run `openclaw-control-panel.exe`.
3. Open `Settings` and configure your environment.
4. Click `Start OpenClaw`.
5. Click `Check Status/Health`.
6. Click `Open Dashboard`.

## Settings Guide

### General

- Language: English / Simplified Chinese / Traditional Chinese
- Theme: Light / Dark

### Mode

- Auto-detect mode can pick an environment automatically.
- Manual override allows forcing one specific mode.

### Paths

Fill paths for your selected deployment mode.

Example formats:

- Windows path: `E:\OPC`
- WSL path: `/mnt/e/OPC`
- WSL Docker project path: `/mnt/e/OPC/openclaw`

### Docker

Common fields:

- Docker compose command (for example: `docker compose`)
- Gateway service name (for example: `openclaw-gateway`)
- Gateway root URL (for example: `http://127.0.0.1:18789`)

### Network

- Proxy mode and custom proxy variables are optional.
- WSL sudo password can be saved with DPAPI encryption from the app UI.

## Runtime Buttons

- `Start OpenClaw`: Starts OpenClaw in current mode.
- `Stop OpenClaw`: Stops OpenClaw in current mode.
- `Open Dashboard`: Opens OpenClaw dashboard (WSL Linux browser flow supported).
- `Check Status/Health`: Checks Docker, gateway container, and HTTP health.

## Troubleshooting

- `docker=down`: Start Docker daemon first (WSL or Windows, depending on your mode).
- `gateway_container=missing`: Run Start once and verify compose service name.
- `http_root=000` / `http_health=000`: Gateway process exists but HTTP endpoint is not reachable yet.
- If using WSL + Clash proxy, set proxy env correctly before bootstrap.

## Integrity Check

Use `SHA256SUMS.txt` to verify release artifacts.

---

# OpenClaw Windows 控制面板（发布版）

这是一个轻量级 Windows 图形控制台，用于在多种部署模式下启动、停止并监控 OpenClaw。

## 支持的部署模式

- `wsl_docker`：OpenClaw 运行在 WSL 内 Docker。
- `wsl_native`：OpenClaw 直接运行在 WSL（无 Docker）。
- `win_docker`：OpenClaw 运行在 Windows Docker。
- `win_native`：OpenClaw 直接运行在 Windows。

## 隐私安全发布说明

- 本发布版**不包含**个人运行时文件。
- 模板中的所有可配置项默认清空。
- 不打包 token、cookie、日志或明文密码。

## 包含文件

- `openclaw-control-panel.exe`
- `openclaw-control-panel.ico`
- `openclaw-start-fast.sh`
- `openclaw-open-dashboard-wsl.sh`
- `openclaw-docker-dashboard.sh`
- `openclaw-approve-pairing.sh`
- `openclaw-control-panel-settings.template.json`
- `SHA256SUMS.txt`

## 快速开始

1. 保持以上文件在同一目录。
2. 运行 `openclaw-control-panel.exe`。
3. 打开 `Settings`，填写你的环境配置。
4. 点击 `Start OpenClaw`。
5. 点击 `Check Status/Health`。
6. 点击 `Open Dashboard`。

## 设置说明

### 常规

- 语言：English / 简体中文 / 繁体中文
- 主题：浅色 / 深色

### 模式

- 自动检测可自动选择运行环境。
- 手动覆盖可强制指定一个模式。

### 路径

按你的部署模式填写对应路径。

格式示例：

- Windows 路径：`E:\OPC`
- WSL 路径：`/mnt/e/OPC`
- WSL Docker 项目路径：`/mnt/e/OPC/openclaw`

### Docker

常见字段：

- Docker compose 命令（如：`docker compose`）
- Gateway 服务名（如：`openclaw-gateway`）
- Gateway 根地址（如：`http://127.0.0.1:18789`）

### 网络

- 代理模式与自定义代理变量均为可选。
- WSL sudo 密码可在应用内以 DPAPI 加密方式保存。

## 运行按钮

- `Start OpenClaw`：按当前模式启动 OpenClaw。
- `Stop OpenClaw`：按当前模式停止 OpenClaw。
- `Open Dashboard`：打开 OpenClaw 仪表板（支持 WSL Linux 浏览器流程）。
- `Check Status/Health`：检测 Docker、gateway 容器和 HTTP 健康状态。

## 故障排查

- `docker=down`：先启动 Docker 守护进程（WSL 或 Windows，取决于模式）。
- `gateway_container=missing`：先执行一次 Start，并确认 compose 服务名配置正确。
- `http_root=000` / `http_health=000`：容器可能已起，但 HTTP 端点暂未就绪。
- 若使用 WSL + Clash 代理，请先正确设置代理环境变量再引导启动。

## 完整性校验

可使用 `SHA256SUMS.txt` 校验发布文件。
