param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
$ErrorActionPreference = 'Stop'

$bridgeScript = Join-Path $PSScriptRoot 'openclaw-wsl-bridge.ps1'
$bridgeInstallScript = Join-Path $PSScriptRoot 'install-openclaw-wsl-bridge.ps1'
$gatewayTaskScript = Join-Path $PSScriptRoot 'openclaw-win-admin-wsl-gateway-task.ps1'
$bridgeTaskName = if ($env:OPENCLAW_WSL_BRIDGE_TASK) { $env:OPENCLAW_WSL_BRIDGE_TASK.Trim() } else { 'OpenClawWslCommandV2' }
$gatewayTaskName = if ($env:OPENCLAW_WSL_GATEWAY_TASK) { $env:OPENCLAW_WSL_GATEWAY_TASK.Trim() } else { 'OpenClawWslGatewayRun' }
$gatewayUrl = if ($env:OPENCLAW_DASHBOARD_URL_BASE) { $env:OPENCLAW_DASHBOARD_URL_BASE.Trim() } else { 'http://127.0.0.1:18790/' }
$distro = if ($env:OPENCLAW_WSL_DISTRO) { $env:OPENCLAW_WSL_DISTRO.Trim() } else { 'Ubuntu-24.04' }
$linuxUser = if ($env:OPENCLAW_WSL_USER) { $env:OPENCLAW_WSL_USER.Trim() } else { 'administrator' }
$linuxHome = if ($env:OPENCLAW_WSL_HOME) { $env:OPENCLAW_WSL_HOME.Trim() } else { '/home/' + $linuxUser }
$gatewayPort = if ($env:OPENCLAW_GATEWAY_PORT) { $env:OPENCLAW_GATEWAY_PORT.Trim() } else { '18790' }
$wslProjectDir = if ($env:OPENCLAW_WSL_PROJECT_DIR) { $env:OPENCLAW_WSL_PROJECT_DIR.Trim() } else { '/opt/openclaw-zh' }
$runtimeConfigPath = if ($env:OPENCLAW_CONFIG_PATH) { $env:OPENCLAW_CONFIG_PATH.Trim() } else { $linuxHome + '/.openclaw/openclaw.cherry.json' }
$runtimeSessionsPath = if ($env:OPENCLAW_MAIN_SESSION_STORE) { $env:OPENCLAW_MAIN_SESSION_STORE.Trim() } else { $linuxHome + '/.openclaw/main.sqlite' }
$healthUrl = $gatewayUrl.TrimEnd('/') + '/health'
$windowsUser = if ($env:OPENCLAW_WSL_WINDOWS_USER) { $env:OPENCLAW_WSL_WINDOWS_USER.Trim() } else { 'Administrator' }
$bridgeStageDir = if ($env:OPENCLAW_WSL_BRIDGE_DIR) { $env:OPENCLAW_WSL_BRIDGE_DIR.Trim() } else { 'C:\Users\' + $windowsUser + '\AppData\Local\OpenClawWslBridge' }

if (-not $gatewayUrl.EndsWith('/')) {
  $gatewayUrl += '/'
}

$env:OPENCLAW_WSL_WINDOWS_USER = $windowsUser
$env:OPENCLAW_WSL_BRIDGE_DIR = $bridgeStageDir
$env:OPENCLAW_WSL_DISTRO = $distro
$env:OPENCLAW_WSL_USER = $linuxUser

function Ensure-BridgeInstalled {
  if (-not (Get-ScheduledTask -TaskName $bridgeTaskName -ErrorAction SilentlyContinue)) {
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $bridgeInstallScript | Out-Null
    if ($LASTEXITCODE -ne 0) {
      throw "Failed to install the OpenClaw WSL bridge task."
    }
  }
}

function Ensure-GatewayTask {
  if (Get-ScheduledTask -TaskName $gatewayTaskName -ErrorAction SilentlyContinue) {
    return
  }

  $actionArgument = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $gatewayTaskScript + '"'
  $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $actionArgument
  $principal = New-ScheduledTaskPrincipal -UserId $windowsUser -LogonType Interactive -RunLevel Highest
  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -StartWhenAvailable
  Register-ScheduledTask -TaskName $gatewayTaskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
}

function Invoke-Bridge {
  param([Parameter(ValueFromRemainingArguments = $true)][string[]]$BridgeArgs)

  Ensure-BridgeInstalled
  $output = & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $bridgeScript @BridgeArgs 2>&1
  [PSCustomObject]@{
    ExitCode = $LASTEXITCODE
    Output = ($output | Out-String).Trim()
  }
}

function Get-HttpCode {
  param([Parameter(Mandatory = $true)][string]$Url)

  try {
    return [string](Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 4).StatusCode
  }
  catch {
    return '000'
  }
}

function Wait-GatewayState {
  param(
    [Parameter(Mandatory = $true)][bool]$Running,
    [int]$TimeoutSec = 20
  )

  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  do {
    $healthy = (Get-HttpCode -Url $healthUrl) -eq '200'
    if ($Running -and $healthy) {
      return $true
    }
    if (-not $Running -and -not $healthy) {
      return $true
    }
    Start-Sleep -Milliseconds 750
  } while ((Get-Date) -lt $deadline)

  return $false
}

function Stop-LingeringGatewayProcesses {
  Get-CimInstance Win32_Process |
    Where-Object {
      $_.Name -in @('wsl.exe', 'bash.exe', 'powershell.exe') -and
      (
        $_.CommandLine -like '*openclaw-win-admin-wsl-gateway-task.ps1*' -or
        $_.CommandLine -like '*openclaw gateway run*'
      )
    } |
    ForEach-Object {
      Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
}

function Start-Gateway {
  Ensure-GatewayTask
  Start-ScheduledTask -TaskName $gatewayTaskName
  return (Wait-GatewayState -Running $true -TimeoutSec 20)
}

function Stop-Gateway {
  if (Get-ScheduledTask -TaskName $gatewayTaskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $gatewayTaskName -ErrorAction SilentlyContinue
  }
  Stop-LingeringGatewayProcesses
  [void](Wait-GatewayState -Running $false -TimeoutSec 10)
}

function Get-GatewayToken {
  $result = Invoke-Bridge config get gateway.auth.token
  if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
    return $result.Output.Trim()
  }
  return 'dev-local-token'
}

function Get-GatewayState {
  if ((Get-HttpCode -Url $healthUrl) -eq '200') {
    return 'running'
  }

  $task = Get-ScheduledTask -TaskName $gatewayTaskName -ErrorAction SilentlyContinue
  if ($task -and $task.State -eq 'Running') {
    return 'starting'
  }

  return 'stopped'
}

function Get-RuntimeDiagnostics {
  return @(
    'runtime_host=windows+wsl2',
    'runtime_backend=openclaw-wsl-bridge',
    ('runtime_bridge_task=' + $bridgeTaskName),
    ('runtime_gateway_task=' + $gatewayTaskName),
    ('runtime_bridge_script=' + $bridgeScript),
    ('runtime_gateway_task_script=' + $gatewayTaskScript),
    ('runtime_wsl_distro=' + $distro),
    ('runtime_wsl_user=' + $linuxUser),
    ('runtime_project_dir=' + $wslProjectDir),
    ('runtime_home_dir=' + $linuxHome),
    ('runtime_config_path=' + $runtimeConfigPath),
    ('runtime_sessions_path=' + $runtimeSessionsPath),
    ('runtime_dashboard_url=' + $gatewayUrl),
    'runtime_diagnose_hint=openclaw-find-runtime-paths.ps1'
  )
}

function Write-StatusBlock {
  param([Parameter(Mandatory = $true)][string]$Action)

  $token = Get-GatewayToken
  $gatewayState = Get-GatewayState
  $httpHealth = Get-HttpCode -Url $healthUrl
  $httpRoot = Get-HttpCode -Url $gatewayUrl
  if ($httpRoot -eq '000' -and $httpHealth -eq '200') {
    $httpRoot = '200'
  }

  Write-Host 'mode=win_native'
  Write-Host ('action=' + $Action)
  Write-Host ('time=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
  Write-Host 'docker=n/a'
  Write-Host ('token=' + $(if ([string]::IsNullOrWhiteSpace($token)) { 'missing' } else { 'ok' }))
  Write-Host ('gateway=' + $gatewayState)
  Write-Host ('gateway_container=' + $gatewayState)
  Write-Host ('http_root=' + $httpRoot)
  Write-Host ('http_health=' + $httpHealth)
  Write-Host 'ok=1'
  foreach ($line in Get-RuntimeDiagnostics) {
    Write-Host $line
  }
}

if ($Args.Count -ge 3 -and $Args[0] -eq 'config' -and $Args[1] -eq 'get' -and $Args[2] -eq 'gateway.auth.token') {
  Write-Output (Get-GatewayToken)
  exit 0
}

if ($Args.Count -ge 2 -and $Args[0] -eq 'gateway' -and $Args[1] -eq 'status') {
  if ((Get-GatewayState) -eq 'running') {
    Write-Output 'Runtime: running'
    Write-Output ('Dashboard: ' + $gatewayUrl)
  }
  else {
    Write-Output 'Runtime: stopped'
  }
  exit 0
}

if (
  ($Args.Count -ge 2 -and $Args[0] -eq 'gateway' -and ($Args[1] -eq 'start' -or $Args[1] -eq 'run')) -or
  ($Args.Count -ge 1 -and $Args[0] -eq 'start')
) {
  $started = Start-Gateway
  Write-StatusBlock 'start'
  exit $(if ($started) { 0 } else { 1 })
}

if (
  ($Args.Count -ge 2 -and $Args[0] -eq 'gateway' -and $Args[1] -eq 'stop') -or
  ($Args.Count -ge 1 -and $Args[0] -eq 'stop')
) {
  Stop-Gateway
  Write-StatusBlock 'stop'
  exit 0
}

if ($Args.Count -ge 1 -and $Args[0] -eq 'status') {
  Write-StatusBlock 'status'
  exit 0
}

if ($Args.Count -ge 1 -and $Args[0] -eq 'dashboard') {
  $token = Get-GatewayToken
  $url = $gatewayUrl + '#token=' + $token
  Start-Process $url | Out-Null
  Write-Output ('dashboard_url=' + $url)
  exit 0
}

if ($Args.Count -ge 1 -and ($Args[0] -eq 'runtime-paths' -or $Args[0] -eq 'where' -or $Args[0] -eq 'doctor.runtime-paths')) {
  foreach ($line in Get-RuntimeDiagnostics) {
    Write-Output $line
  }
  exit 0
}

$result = Invoke-Bridge @Args
if ($result.Output) {
  Write-Output $result.Output
}
exit $result.ExitCode
