# Admin WSL Bundle

这个目录现在可以作为一个“建议版”的 Windows GUI 交付包来用，核心思路是：

- GUI 继续复用现成的 [openclaw-control-panel.exe](/home/lanla/openclaw-windows-panel/openclaw-control-panel.exe)
- `win_native` 模式不再直接碰旧的 Windows Native 逻辑
- 改由一套新的 `Windows -> Scheduled Task -> WSL -> openclaw` 桥接脚本接管

## 适用场景

- OpenClaw 已经装在 `WSL Ubuntu-24.04`
- Linux 用户是 `administrator`
- 网关默认端口是 `18790`
- 需要一个 Windows 侧能点按钮的 GUI，而不是每次手敲命令

## 交付内容

- `openclaw-control-panel.exe`
- `openclaw-win-admin-wsl-entry.ps1`
- `openclaw-win-admin-wsl-install.ps1`
- `openclaw-win-admin-wsl-gateway-task.ps1`
- `openclaw-wsl-bridge.ps1`
- `openclaw-wsl-bridge-runner.ps1`
- `install-openclaw-wsl-bridge.ps1`
- `new-admin-wsl-panel-settings.ps1`
- `openclaw-find-runtime-paths.ps1`

## Windows 侧建议步骤

1. 把整个 bundle 放在同一个文件夹里。
2. 用管理员 PowerShell 运行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\install-openclaw-wsl-bridge.ps1
.\new-admin-wsl-panel-settings.ps1
```

3. 双击运行 `openclaw-control-panel.exe`
4. 在面板里使用 `win_native` 模式

## 这套 GUI 现在接通的能力

- Start / Stop
  - 通过 `OpenClawWslGatewayRun` 计划任务管理 WSL 内的 `openclaw gateway run`
- Status / Health
  - 通过 `http://127.0.0.1:18790/health` 探测
- Open Dashboard
  - 自动拼接 token 后打开浏览器
- Runtime diagnostics
  - 可输出 bridge task、gateway task、WSL distro、用户、project dir 和 config path
- 通用 CLI bridge
  - 除 GUI 固定动作外，其余参数会透传给 WSL 里的 `openclaw`

## 已知限制

- 这个 bundle 复用的是已有 `.exe`，不是新源码重编译版 GUI。
- 因为当前仓库没有这款 GUI 的 C# 源码，所以这里做的是“可交付的推荐发行包”，不是改 GUI 按钮布局。
- 如果你要真正新增 GUI 按钮或页面，需要拿到原 GUI 的源码再改。
