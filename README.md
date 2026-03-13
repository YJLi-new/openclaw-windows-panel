<div align="center">
  <img src="banner.jpg" alt="OpenClaw Control Panel Banner" width="100%" />
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
- 🔎 **真实运行目录诊断 (Runtime Path Diagnostics)**

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

### 先找真实生效目录，再改配置

在真实使用里，一个很常见的问题不是“不会改配置”，而是**改错了那份配置文件**。

当前发布版里，您至少要区分两类路径：

- **包目录**
  - 也就是 `openclaw-control-panel.exe` 所在目录
  - 这里可能存在一份 `openclaw-control-panel-settings.json`
- **运行时真实 settings 路径**
  - 在我们排查到的当前发布版里，常见真实路径是 `E:\OPC\panel\openclaw-control-panel-settings.json`
  - 如果这一份和 exe 同目录那份不一致，面板很可能继续读取 `E:\OPC\panel\...`，导致您“明明改了配置但界面没变”

建议先运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\openclaw-find-runtime-paths.ps1
```

这个脚本会直接告诉您：

- 包目录在哪里
- exe 是否存在
- exe 同目录 settings 文件是哪一份
- 当前发布版常见的 active settings 文件是否存在
- 两份 settings 在关键字段上是否已经漂移
- 当前 gateway 端口是不是由 `wslrelay.exe` 持有
- 如果实际运行体在 WSL 中，真正生效的项目目录、数据目录、`openclaw.json` 和会话存储路径分别在哪里

### 面板里的诊断入口现在在哪里

当前这个发布仓库只包含已编译的 `openclaw-control-panel.exe`，不包含 C# 源码，所以这里**不能直接新增一个新的 GUI 按钮**。

这次发布里，真实运行目录诊断被落在了两个现有入口里：

- `检查状态 / Health`
  - 如果您使用随包的 `openclaw-wsl-native-helper.sh` 或 `openclaw-win-native-entry.ps1`
  - 状态输出现在会额外打印 `runtime_*` 字段，例如：
    - `runtime_project_dir`
    - `runtime_data_dir`
    - `runtime_config_path`
    - `runtime_sessions_path`
- `openclaw-find-runtime-paths.ps1`
  - 这是独立的“深度诊断入口”
  - 它不仅比较包目录 settings 和 active settings，还会尝试读出 live WSL runtime

换句话说，现在不用再靠猜：

- 面板里点一次“检查状态/健康度”，就能先看到当前真实运行目录
- 仍不确定时，再跑 `openclaw-find-runtime-paths.ps1`

额外注意两件很容易踩坑的地方：

- 顶部启动日志里的“项目目录 / Project dir”
  - 在我们排查到的当前构建里，它更接近 `profiles.wsl_docker.windows_project_dir`
  - 不是单纯读取 `profiles.win_native.win_native_project_dir`
- Dashboard 地址
  - 当前构建期望的是 `dashboard.gateway_root_url`
  - 不是旧模板里的 `docker.gateway_root_url`

### WSL Native 推荐配置

如果您使用 `wsl_native` 模式，并且 OpenClaw 已经在 WSL 内以 systemd user service 方式运行，推荐额外准备两份 WSL 内脚本：

- `openclaw-wsl-native-helper.sh`
  - 作为 `wsl_native_openclaw_command`
  - 提供 `config get gateway.auth.token`、`gateway status/start/stop/run` 和 `status` 兼容层
- `openclaw-wsl-native-dashboard.sh`
  - 作为 WSL 内打开 Dashboard 的脚本
  - 自动读取 `.gateway-token`，优先通过 `powershell.exe Start-Process` 调起 Windows 默认浏览器；若不可用再回退到 `explorer.exe` / `wslview`

推荐字段如下：

```json
{
  "profiles": {
    "wsl_docker": {
      "windows_project_dir": "\\\\wsl$\\Ubuntu-24.04\\home\\<user>\\src\\openclaw",
      "wsl_project_dir": "/home/<user>/src/openclaw",
      "wsl_docker_dashboard_script": "/home/<user>/bin/openclaw-wsl-native-dashboard.sh"
    },
    "wsl_native": {
      "wsl_native_project_dir": "/home/<user>/src/openclaw",
      "wsl_native_openclaw_command": "/home/<user>/bin/openclaw-wsl-native-helper.sh",
      "wsl_native_install_command": "/home/<user>/bin/openclaw-wsl-native-helper.sh gateway start"
    }
  },
  "dashboard": {
    "gateway_root_url": "http://127.0.0.1:18789/"
  },
  "network": {
    "proxy_mode": "off"
  }
}
```

> `proxy_mode=off` 对 `wsl_native` 尤其重要：如果 WSL 里继承了 Windows 代理环境，`127.0.0.1`/`localhost` 健康探测很容易被误判成 `000`。

### Windows Native 兼容包装层

如果当前发布版里 `wsl_native` 分支行为不稳定，但您的 OpenClaw 实际仍运行在 WSL 中，可以把 `win_native` 配成一个 **Windows 侧 PowerShell 包装层**，由它再通过 `wsl.exe` 驱动 WSL 内的 gateway：

- `win_native_project_dir`
  - 可直接填 `\\wsl$\Ubuntu-24.04\home\<user>\src\openclaw`
- `win_native_openclaw_command`
  - 指向 `openclaw-win-native-entry.ps1`
- `win_native_install_command`
  - 指向 `openclaw-win-native-install.ps1`

这对脚本支持的行为与面板当前二进制所需接口对齐：

- `config get gateway.auth.token`
- `gateway status/start/stop/run`
- `status`
- `dashboard`

如需指定非默认发行版或 Linux 用户，可在启动面板前设置以下环境变量：

- `OPENCLAW_WSL_DISTRO`
- `OPENCLAW_WSL_USER`
- `OPENCLAW_DASHBOARD_URL_BASE`
- `OPENCLAW_WSL_NATIVE_SERVICE`

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
- 🐧 **`openclaw-wsl-native-helper.sh`** (WSL 原生状态/启停兼容层)
  - 适用于 `wsl_native` 模式已由 systemd user service 托管 OpenClaw 的场景。
  - 兼容面板需要的 `config get gateway.auth.token`、`gateway status/start/stop/run` 调用，并附带一个轻量 sentinel 进程，避免 WSL 原生模式被误判为 `gateway=stopped`。
  - `status` 输出会附带 `runtime_*` 诊断字段，直接告诉您当前真实项目目录、数据目录和配置路径。
- 🧭 **`openclaw-wsl-native-dashboard.sh`** (WSL 原生一键打开 Dashboard)
  - 面向 `wsl_native` 模式的轻量 opener，自动读取 token，优先使用 `powershell.exe Start-Process` 调起 Windows 默认浏览器，并回退到 `explorer.exe` / `wslview`。
- 🪟 **`openclaw-win-native-entry.ps1`** (Windows 原生兼容入口)
  - 适用于面板运行在 Windows、但实际 OpenClaw gateway 仍在 WSL 里的场景。
  - 通过 `wsl.exe` 桥接 `config get gateway.auth.token`、`gateway status/start/stop/run`、`dashboard` 等调用。
  - `status` 输出同样会附带 `runtime_*` 诊断字段，避免把 `E:\OPC` 之类的面板目录误认成真实 OpenClaw 运行目录。
- 🛠️ **`openclaw-win-native-install.ps1`** (Windows 原生兼容安装命令)
  - 面向 `win_native_install_command` 的轻量包装，内部调用 `openclaw-win-native-entry.ps1 start`。
- 🔎 **`openclaw-find-runtime-paths.ps1`** (定位真实运行时目录 / settings)
  - 用于确认当前应该改哪一份 settings 文件，避免只改了 exe 同目录文件却完全不生效。
  - 同时会提示 `windows_project_dir`、`win_native_project_dir` 和 `dashboard.gateway_root_url` 这些容易混淆的字段。
  - 新版还会尝试确认 live gateway 监听进程，以及 WSL 中真实生效的 `project dir`、`data dir`、`openclaw.json` 和 `sessions.json`。
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
  - 如果您使用 `wsl_native`，请优先检查 `dashboard.gateway_root_url` 是否填写在正确字段中，而不是旧版模板里的 `docker.gateway_root_url`。
  - 如果您在 Windows / WSL 中启用了系统代理，建议将 `network.proxy_mode` 显式设为 `off`，避免 `localhost` 请求被代理劫持而误判成 `000`。
- ❌ **明明改了 JSON，但界面还是旧目录 / 旧地址**
  - 先运行 `openclaw-find-runtime-paths.ps1`。
  - 很多时候不是字段写错，而是改到了 exe 同目录的那份 settings；当前发布版实际还在读取另一份 active settings。
- ❌ **顶部“项目目录”已经不对，但 `win_native_project_dir` 明明改过了**
  - 先检查 `profiles.wsl_docker.windows_project_dir`。
  - 在我们排查到的当前构建里，顶部显示更接近这个字段，而不是 `profiles.win_native.win_native_project_dir`。
- ❌ **WSL 环境下的代理问题**
  - 如果您在 Windows 主机上使用代理软件（如 Clash），请确保 WSL 内的代理环境变量设置正确。除非您手动禁用，否则引导脚本可能会尝试在 WSL 中执行自动代理配置。
- ❌ **WSL 迁移到非系统盘后报 `WSL_E_ACCESSDENIED`**
  - 这通常不是 OpenClaw 本身的问题，而是 WSL VHDX 所在目录的 ACL 被破坏了。请先修复 `E:\WSL\...` 的权限，再继续排查面板。
- ❌ **自己从 PowerShell 生成 WSL shell 脚本后报 `syntax error: unexpected end of file`**
  - 高概率是脚本被写成了 `CRLF`。请在 WSL 中执行 `tr -d '\r' < broken.sh > fixed.sh` 或用 `sed -i 's/\r$//' broken.sh` 统一转成 `LF`。

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
