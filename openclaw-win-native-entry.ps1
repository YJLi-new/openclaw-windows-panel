param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Args)
$ErrorActionPreference = 'Stop'

$wslExe = Join-Path $env:SystemRoot 'System32\wsl.exe'
$gatewayUrl = if ($env:OPENCLAW_DASHBOARD_URL_BASE) { $env:OPENCLAW_DASHBOARD_URL_BASE.Trim() } else { 'http://127.0.0.1:18789/' }
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

function Get-GatewayToken {
  $result = Invoke-WslCapture "if [ -f ~/.openclaw/.gateway-token ] && [ -s ~/.openclaw/.gateway-token ]; then tr -d '\r\n' < ~/.openclaw/.gateway-token; else printf 'dev-local-token'; fi"
  if ($result.ExitCode -eq 0 -and -not [string]::IsNullOrWhiteSpace($result.Output)) {
    return $result.Output
  }
  return 'dev-local-token'
}

function Get-HttpCode {
  param([string]$Url)
  try {
    return [string](Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 4).StatusCode
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
}

function Start-Gateway {
  $result = Invoke-WslCapture "systemctl --user restart $serviceName; systemctl --user is-active $serviceName || true"
  Start-Sleep -Seconds 2
  return $result.Output -match 'active'
}

function Stop-Gateway {
  Invoke-WslCapture "systemctl --user stop $serviceName || true" | Out-Null
}

if ($Args.Count -ge 3 -and $Args[0] -eq 'config' -and $Args[1] -eq 'get' -and $Args[2] -eq 'gateway.auth.token') {
  Write-Output (Get-GatewayToken)
  exit 0
}

if ($Args.Count -ge 2 -and $Args[0] -eq 'gateway' -and $Args[1] -eq 'status') {
  if ((Get-GatewayState) -eq 'running') {
    Write-Output 'Runtime: running'
    Write-Output ('Dashboard: ' + $gatewayUrl)
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
  $token = Get-GatewayToken
  $url = $gatewayUrl + '#token=' + $token
  Start-Process $url | Out-Null
  Write-Output ('dashboard_url=' + $url)
  exit 0
}

$action = if ($Args.Count -gt 0) { $Args[0] } else { 'status' }
Write-StatusBlock $action
exit 0
