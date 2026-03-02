<div align="center">
  <img src="openclaw-control-panel.ico" alt="OpenClaw Control Panel Logo" width="120" height="120" />
  <h1>OpenClaw Windows 控制面板 🎛️</h1>

  <p>
    <b>告别繁琐的终端命令！</b>这是一款专为 Windows 用户打造的轻量级 <b>GUI 工具</b>，旨在帮助您轻松启动、停止、检查运行状态，并一键打开 OpenClaw 控制台 UI。<br>
    无论您使用 <b>WSL、Docker 还是原生环境</b>，都能获得统一、流畅的管理体验。
  </p>

  <p>
    <a href="https://github.com/YJLi-new/openclaw-control-panel-release/releases">
      <img alt="GitHub release" src="https://img.shields.io/github/v/release/YJLi-new/openclaw-control-panel-release?sort=semver&style=flat-square&color=blue" />
    </a>
    <img alt="Platform" src="https://img.shields.io/badge/Platform-Windows-0078D6?style=flat-square&logo=windows" />
    <img alt="Modes" src="https://img.shields.io/badge/Modes-WSL%20%7C%20Docker%20%7C%20Native-informational?style=flat-square" />
    <img alt="UI language" src="https://img.shields.io/badge/Languages-EN%20%7C%20ZH--Hans%20%7C%20ZH--Hant-brightgreen?style=flat-square" />
  </p>

</div>

---

## ✨ 为什么选择它？

OpenClaw 支持多种运行环境（WSL、Docker 或 Windows 原生）。但在不同环境间切换通常意味着：
- 😫 需要记住该用哪个终端
- 🔍 频繁在不同文件夹中来回翻找
- ⌨️ 反复输入冗长相同的命令
- 🔑 手动打开控制面板并处理繁琐的授权配对

**OpenClaw Windows 控制面板** 将这些高频操作聚合到了一个直观的窗口中：
- 🟢 **一键启停 (Start / Stop)**
- 🩺 **状态与健康检查 (Status / Health)**
- 🌐 **一键直达控制台 (Open Dashboard)**

---

## 📦 支持的部署模式

无论您习惯哪种工作流，我们都为您提供了适配方案：

| 模式标识 | OpenClaw 运行位置 | 适用场景说明 |
| :--- | :--- | :--- |
| 🐳 `wsl_docker` | **WSL 内部的 Docker** | Windows 用户的主流选择，项目文件存储于 WSL 中 |
| 🐧 `wsl_native` | **WSL 原生环境** (无 Docker) | 偏好纯正 Linux 体验和 CLI 工作流的用户 |
| 🪟 `win_docker` | **Windows 上的 Docker** | 使用 Windows 版 Docker Desktop（不依赖 WSL） |
| 💻 `win_native` | **Windows 原生环境** | 直接在 Windows 系统本地运行 OpenClaw |

---

## 🚀 极速上手 (只需 3 分钟)

1. **📥 下载程序**
   - 推荐前往 **[Releases 页面](https://github.com/YJLi-new/openclaw-control-panel-release/releases)** 下载最新版本。
   - 或下载本仓库的特定 Tag 版本（例如 `v1.0.1`）。
2. **📁 保持文件完整**
   - 请将解压后的所有文件保存在**同一个文件夹**内（⚠️ 请勿将 `.exe` 文件与脚本/模板分开放置）。
3. **▶️ 运行程序**
   - 双击运行 `openclaw-control-panel.exe`。
4. **⚙️ 初始配置**
   - 打开 **Settings (设置)**，根据您的部署模式填写对应字段（强烈建议优先尝试“Auto-Detect / 智能检测”）。
5. **🖱️ 开始使用**
   - 点击面板上的按钮即可轻松完成启动、健康检查和打开仪表盘的操作。

> 💡 **提示**：OpenClaw 控制台 UI 通常运行在 `http://127.0.0.1:18789/`（具体取决于网关配置）。<br>
> 官方控制台文档详见：[OpenClaw Docs](https://docs.openclaw.ai/web/control-ui)

---

## 🔒 隐私与安全警示 (必读)

### 隐私保护设计
本仓库被设计为绝对干净的公共发布版：
- ✅ **配置文件留白**：自带安全的空白模板 `openclaw-control-panel-settings.template.json`。
- ✅ **零敏感信息泄露**：绝不捆绑 Tokens、Cookies、明文密码或日志。

程序运行时生成的配置文件（如 `*.private.json`）、日志（`*.log`）以及 `.gateway-token` 等敏感文件，已被 `.gitignore` 规则刻意忽略，**请绝对不要将它们上传或分享给他人**！

### ⚠️ 核心安全注意事项
OpenClaw 的控制台 UI 采用了 **Token/密码验证** + **设备身份配对** 的双重机制来防止未经授权的访问。

- 访问限制：除 `127.0.0.1` 的本地访问（自动批准）外，局域网/Tailnet 等远程访问通常需要对新设备进行**配对批准**。
- **高危操作提醒**：辅助脚本 `openclaw-docker-dashboard.sh` 会显式开启 `dangerouslyDisableDeviceAuth = true`（禁用设备身份验证）。虽然这在纯本地工作流中非常方便，但**如果您将网关暴露在公网或不受信的网络中，这将导致严重的安全降级**！

🛡️ **官方建议**：除非您非常清楚自己在做什么，否则请让控制台 UI 仅绑定在 localhost。如需远程访问，请务必遵循官方的安全建议（例如使用 Tailscale Serve 提供的 HTTPS）。

---

## 🛠️ 高级辅助脚本

<details>
<summary>如果您只需要使用 GUI 面板，可以安全地<b>忽略此部分</b>。点击展开查看高级脚本详情 👉</summary>

为满足高阶玩家的需求，本次发布还包含了以下快捷脚本：

- ⚡ **`openclaw-start-fast.sh`** (WSL Docker 极速启动)
  - 确保 WSL 内部的 Docker 守护进程正常运行，并通过 `docker compose` 启动网关服务。
- 🏗️ **`openclaw-docker-dashboard.sh`** (引导+上线+启动全家桶)
  - 执行完整的引导流程：创建目录、生成 token、写入 `.env`，并在非交互模式下启动网关。
- 🌐 **`openclaw-open-dashboard-wsl.sh`** (在 WSL 中调起 Linux 浏览器)
  - 自动读取 Token，以独立 Profile 启动 Linux 版 Chrome 并附加 Token URL，支持配置中文输入法 (fcitx5) 及 CDP 端口。
- ✅ **`openclaw-approve-pairing.sh`** (一键批准设备配对)
  - 通过 `docker compose exec` 进入网关容器，自动批准所有挂起的设备配对请求。

</details>

---

## 💡 常见问题排查 (Troubleshooting)

- ❌ **提示 `docker=down`**
  - 请确认并启动与您当前模式对应的 Docker 守护进程（WSL Docker 或 Windows Docker）。
- ❌ **提示 `gateway_container=missing`**
  - 请尝试启动一次服务，然后检查并确认您的 compose 服务名称是否正确（默认示例通常为 `openclaw-gateway`）。
- ❌ **提示 `http_root=000` 或 `http_health=000`**
  - 容器进程可能已启动，但 HTTP 服务仍在初始化或无法访问。请等待几秒钟后重试，并确认 `gateway_root_url` 配置无误。
- ❌ **WSL 环境下的代理问题**
  - 如果您在 Windows 主机上使用代理软件（如 Clash），请确保 WSL 内的代理环境变量设置正确。除非您手动禁用，否则引导脚本可能会尝试在 WSL 中执行自动代理配置。

---

## 🔐 文件完整性校验

为确保您的下载安全未被篡改，我们在发布包中提供了 `SHA256SUMS.txt`。
建议 Windows 用户使用以下 PowerShell 脚本进行校验：

```powershell
$raw = (Get-Content .\SHA256SUMS.txt -Raw)
$parts = $raw.Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)

for ($i=0; $i -lt $parts.Count; $i+=2) {
  $expect = $parts[$i].ToLower()
  $file = $parts[$i+1]

  if (!(Test-Path $file)) { Write-Host "MISS  $file"; continue }
  $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLower()

  if ($actual -eq $expect) { Write-Host "OK    $file" }
  else {
    Write-Host "FAIL  $file"
    Write-Host "  expect=$expect"
    Write-Host "  actual=$actual"
  }
}
