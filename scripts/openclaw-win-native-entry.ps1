param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
$ErrorActionPreference = 'Stop'

$wslExe = Join-Path $env:SystemRoot 'System32\wsl.exe'
$gatewayUrl = if ($env:OPENCLAW_DASHBOARD_URL_BASE) { $env:OPENCLAW_DASHBOARD_URL_BASE.Trim() } else { 'http://127.0.0.1:18790/' }
if (-not $gatewayUrl.EndsWith('/')) { $gatewayUrl += '/' }
$healthUrl = $gatewayUrl.TrimEnd('/') + '/health'
$serviceName = if ($env:OPENCLAW_WSL_NATIVE_SERVICE) { $env:OPENCLAW_WSL_NATIVE_SERVICE.Trim() } else { 'openclaw-gateway.service' }
$distro = if ($env:OPENCLAW_WSL_DISTRO) { $env:OPENCLAW_WSL_DISTRO.Trim() } else { '' }
$linuxUser = if ($env:OPENCLAW_WSL_USER) { $env:OPENCLAW_WSL_USER.Trim() } else { '' }

function Resolve-WslInvocationPrefix {
  $argsList = @()
  if (-not [string]::IsNullOrWhiteSpace($distro)) {
    $argsList += @('-d', $distro)
  }
  if (-not [string]::IsNullOrWhiteSpace($linuxUser)) {
    $argsList += @('-u', $linuxUser)
  }
  return $argsList
}

function Get-ControlUiUrl {
  if ($env:OPENCLAW_CONTROL_UI_URL_BASE) {
    return $env:OPENCLAW_CONTROL_UI_URL_BASE.Trim()
  }
  return $gatewayUrl.Trim()
}

function Invoke-WslCapture {
  param([string]$Command)
  $invokeArgs = Resolve-WslInvocationPrefix
  $invokeArgs += @('--', 'bash', '-lc', $Command)
  $output = & $wslExe @invokeArgs 2>&1
  [PSCustomObject]@{
    ExitCode = $LASTEXITCODE
    Output = ($output | Out-String).Trim()
  }
}

function Get-BridgeDashboardUrl {
  $result = Invoke-WslCapture 'openclaw dashboard --no-open'
  if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
    foreach ($line in ($result.Output -split "`r?`n")) {
      $trimmed = $line.Trim()
      if ($trimmed -match '^Dashboard URL:\s*(\S+)$') {
        return $Matches[1]
      }
      if ($trimmed -match '^dashboard_url=(\S+)$') {
        return $Matches[1]
      }
      if ($trimmed -match '^https?://\S+$') {
        return $trimmed
      }
    }
  }
  return ''
}

function Quote-WslLiteral {
  param([string]$Value)
  return "'" + $Value.Replace("'", "'""'""'") + "'"
}

function Get-GatewayToken {
  $dashboardUrl = Get-BridgeDashboardUrl
  if (-not [string]::IsNullOrWhiteSpace($dashboardUrl)) {
    if ($dashboardUrl -match '[?#]token=([^&]+)') {
      return $Matches[1]
    }
  }

  $result = Invoke-WslCapture "if [ -f ~/.openclaw/.gateway-token ] && [ -s ~/.openclaw/.gateway-token ]; then tr -d '\r\n' < ~/.openclaw/.gateway-token; else printf ''; fi"
  if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
    if ($result.Output -ne '__OPENCLAW_REDACTED__' -and $result.Output -ne 'dev-local-token') {
      return $result.Output
    }
  }
  return ''
}

function Get-HttpCode {
  param([string]$Url)
  try {
    return [string](Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 4).StatusCode
  } catch [System.Net.WebException] {
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
      return [string][int]$_.Exception.Response.StatusCode
    }
    return '000'
  } catch {
    return '000'
  }
}

function Get-GatewayState {
  $result = Invoke-WslCapture "systemctl --user is-active $serviceName || true"
  if ($result.Output -match 'active') {
    return 'running'
  }
  return 'stopped'
}

function Test-WslFileExists {
  param([string]$Path)
  $quoted = Quote-WslLiteral $Path
  $result = Invoke-WslCapture "if [ -f $quoted ]; then printf 'yes'; else printf 'no'; fi"
  return $result.Output -eq 'yes'
}

function Get-WslHome {
  $result = Invoke-WslCapture 'printf %s "$HOME"'
  return $result.Output
}

function Get-WslUser {
  $result = Invoke-WslCapture 'whoami'
  return $result.Output
}

function Get-WslDistroName {
  $result = Invoke-WslCapture 'printf %s "${WSL_DISTRO_NAME:-}"'
  if (-not [string]::IsNullOrWhiteSpace($result.Output)) {
    return $result.Output
  }
  if (-not [string]::IsNullOrWhiteSpace($distro)) {
    return $distro
  }
  return 'unknown'
}

function Get-WslExecStart {
  $result = Invoke-WslCapture "systemctl --user show $serviceName --property ExecStart --value 2>/dev/null || true"
  return $result.Output
}

function Resolve-WslProjectDir {
  if (-not [string]::IsNullOrWhiteSpace($env:OPENCLAW_WSL_PROJECT_DIR)) {
    return $env:OPENCLAW_WSL_PROJECT_DIR.Trim()
  }

  $execStart = Get-WslExecStart
  if ($execStart -match '(/[^ ;]+?)/dist/index\.js') {
    return $Matches[1]
  }

  $home = Get-WslHome
  if ([string]::IsNullOrWhiteSpace($home)) {
    return ''
  }

  foreach ($candidate in @(
    "$home/src/openclaw-cn",
    "$home/src/openclaw",
    "$home/openclaw-cn",
    "$home/openclaw"
  )) {
    $quoted = Quote-WslLiteral $candidate
    $result = Invoke-WslCapture "if [ -d $quoted ]; then printf %s $quoted; fi"
    if (-not [string]::IsNullOrWhiteSpace($result.Output)) {
      return $result.Output
    }
  }

  return ''
}

function Get-RuntimeDiagnostics {
  $home = Get-WslHome
  $userName = Get-WslUser
  $distroName = Get-WslDistroName
  $projectDir = Resolve-WslProjectDir
  $dataDir = if ($env:OPENCLAW_DATA_DIR) { $env:OPENCLAW_DATA_DIR.Trim() } elseif (-not [string]::IsNullOrWhiteSpace($home)) { "$home/.openclaw" } else { '~/.openclaw' }
  $configPath = if ($env:OPENCLAW_CONFIG_PATH) { $env:OPENCLAW_CONFIG_PATH.Trim() } else { "$dataDir/openclaw.json" }
  $sessionsPath = if ($env:OPENCLAW_MAIN_SESSION_STORE) { $env:OPENCLAW_MAIN_SESSION_STORE.Trim() } else { "$dataDir/agents/main/sessions/sessions.json" }
  $execStart = Get-WslExecStart

  $lines = @(
    'runtime_host=wsl2',
    ('runtime_service=' + $serviceName),
    ('runtime_user=' + $(if ([string]::IsNullOrWhiteSpace($userName)) { 'unknown' } else { $userName })),
    ('runtime_distro=' + $distroName),
    ('runtime_project_dir=' + $(if ([string]::IsNullOrWhiteSpace($projectDir)) { 'unknown' } else { $projectDir })),
    ('runtime_data_dir=' + $dataDir),
    ('runtime_config_path=' + $configPath),
    ('runtime_config_exists=' + $(if (Test-WslFileExists $configPath) { 'yes' } else { 'no' })),
    ('runtime_sessions_path=' + $sessionsPath),
    ('runtime_sessions_exists=' + $(if (Test-WslFileExists $sessionsPath) { 'yes' } else { 'no' })),
    ('runtime_dashboard_url=' + (Get-ControlUiUrl)),
    'runtime_diagnose_hint=openclaw-find-runtime-paths.ps1'
  )

  if (-not [string]::IsNullOrWhiteSpace($execStart)) {
    $lines += ('runtime_exec_start=' + $execStart)
  }

  return $lines
}

function Write-StatusBlock {
  param([string]$Action)

  $token = Get-GatewayToken
  $gatewayState = Get-GatewayState
  $httpHealth = Get-HttpCode $healthUrl
  $httpRoot = Get-HttpCode $gatewayUrl

  if ($httpRoot -eq '000') {
    $httpRoot = Get-HttpCode ($gatewayUrl + 'chat?session=main')
  }
  if ($httpRoot -eq '000' -and $httpHealth -eq '200') {
    $httpRoot = '200'
  }

  Write-Host 'mode=win_native'
  Write-Host ('action=' + $Action)
  Write-Host ('time=' + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'))
  Write-Host 'docker=n/a'
  if ([string]::IsNullOrWhiteSpace($token)) {
    Write-Host 'token=missing'
  } else {
    Write-Host 'token=ok'
  }
  Write-Host ('gateway=' + $gatewayState)
  Write-Host ('gateway_container=' + $gatewayState)
  Write-Host ('http_root=' + $httpRoot)
  Write-Host ('http_health=' + $httpHealth)
  Write-Host 'ok=1'
  foreach ($line in Get-RuntimeDiagnostics) {
    Write-Host $line
  }
}

function Start-Gateway {
  $result = Invoke-WslCapture "systemctl --user restart $serviceName; systemctl --user is-active $serviceName || true"
  Start-Sleep -Seconds 2
  return $result.Output -match 'active'
}

function Stop-Gateway {
  Invoke-WslCapture "systemctl --user stop $serviceName || true" | Out-Null
}

function Open-DashboardUrl {
  param([Parameter(Mandatory = $true)][string]$Url)

  try {
    Start-Process $Url | Out-Null
    return
  }
  catch {
  }

  try {
    Start-Process -FilePath 'rundll32.exe' -ArgumentList 'url.dll,FileProtocolHandler', $Url | Out-Null
    return
  }
  catch {
  }

  try {
    & explorer.exe $Url | Out-Null
    return
  }
  catch {
  }
}

if ($Args.Count -ge 3 -and $Args[0] -eq 'config' -and $Args[1] -eq 'get' -and $Args[2] -eq 'gateway.auth.token') {
  Write-Output (Get-GatewayToken)
  exit 0
}

if ($Args.Count -ge 2 -and $Args[0] -eq 'gateway' -and $Args[1] -eq 'status') {
  if ((Get-GatewayState) -eq 'running') {
    Write-Output 'Runtime: running'
    Write-Output ('Dashboard: ' + (Get-ControlUiUrl))
  } else {
    Write-Output 'Runtime: stopped'
  }
  exit 0
}

if (
  ($Args.Count -ge 2 -and $Args[0] -eq 'gateway' -and ($Args[1] -eq 'start' -or $Args[1] -eq 'run')) -or
  ($Args.Count -ge 1 -and $Args[0] -eq 'start')
) {
  if (Start-Gateway) {
    Write-StatusBlock 'start'
    exit 0
  }
  Write-StatusBlock 'start'
  exit 1
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
  $url = Get-BridgeDashboardUrl
  if ([string]::IsNullOrWhiteSpace($url)) {
    $token = Get-GatewayToken
    $url = Get-ControlUiUrl
    if (-not [string]::IsNullOrWhiteSpace($token)) {
      $url += '#token=' + $token
    }
  }
  Open-DashboardUrl -Url $url
  Write-Output ('dashboard_url=' + $url)
  exit 0
}

if ($Args.Count -ge 1 -and ($Args[0] -eq 'runtime-paths' -or $Args[0] -eq 'where' -or $Args[0] -eq 'doctor.runtime-paths')) {
  foreach ($line in Get-RuntimeDiagnostics) {
    Write-Output $line
  }
  exit 0
}

$action = if ($Args.Count -gt 0) { $Args[0] } else { 'status' }
Write-StatusBlock $action
exit 0
