[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$runnerPath = Join-Path $PSScriptRoot 'openclaw-wsl-bridge-runner.ps1'
$taskName = if ($env:OPENCLAW_WSL_BRIDGE_TASK) { $env:OPENCLAW_WSL_BRIDGE_TASK.Trim() } else { 'OpenClawWslCommandV2' }
$stageDir = if ($env:OPENCLAW_WSL_BRIDGE_DIR) { $env:OPENCLAW_WSL_BRIDGE_DIR.Trim() } else { Join-Path $env:LOCALAPPDATA 'OpenClawWslBridge' }
$wslExe = Join-Path $env:SystemRoot 'System32\wsl.exe'
$windowsUser = if ($env:OPENCLAW_WSL_WINDOWS_USER) { $env:OPENCLAW_WSL_WINDOWS_USER.Trim() } else { [System.Security.Principal.WindowsIdentity]::GetCurrent().Name }

if (-not (Test-Path $runnerPath)) {
  throw "Runner script was not found: $runnerPath"
}

if (-not (Test-Path $wslExe)) {
  throw "wsl.exe was not found at $wslExe"
}

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$actionArgument = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $runnerPath + '"'
$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $actionArgument
$principal = New-ScheduledTaskPrincipal -UserId $windowsUser -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -StartWhenAvailable

Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null

$distroList = (& $wslExe -l -q 2>$null | ForEach-Object { $_.Trim() } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$report = [ordered]@{
  task_name = $taskName
  task_user = $windowsUser
  stage_dir = $stageDir
  runner_script = $runnerPath
  wsl_exe = $wslExe
  wsl_distros = $distroList
}

$report | ConvertTo-Json -Depth 4
