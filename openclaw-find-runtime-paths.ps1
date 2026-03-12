[CmdletBinding()]
param(
  [switch]$AsJson
)

$ErrorActionPreference = 'Stop'

function Get-JsonValue {
  param(
    [object]$Object,
    [string[]]$PathParts
  )

  $current = $Object
  foreach ($part in $PathParts) {
    if ($null -eq $current) { return $null }
    $prop = $current.PSObject.Properties[$part]
    if ($null -eq $prop) { return $null }
    $current = $prop.Value
  }
  return $current
}

function Read-SettingsSummary {
  param([string]$Path)

  if (-not (Test-Path -LiteralPath $Path)) {
    return [pscustomobject]@{
      path = $Path
      exists = $false
    }
  }

  try {
    $json = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return [pscustomobject]@{
      path = $Path
      exists = $true
      schema_version = Get-JsonValue $json @('schema_version')
      mode_manual_override = Get-JsonValue $json @('mode', 'manual_override')
      mode_last_detected = Get-JsonValue $json @('mode', 'last_detected')
      windows_project_dir = Get-JsonValue $json @('profiles', 'wsl_docker', 'windows_project_dir')
      wsl_project_dir = Get-JsonValue $json @('profiles', 'wsl_docker', 'wsl_project_dir')
      wsl_native_project_dir = Get-JsonValue $json @('profiles', 'wsl_native', 'wsl_native_project_dir')
      wsl_native_openclaw_command = Get-JsonValue $json @('profiles', 'wsl_native', 'wsl_native_openclaw_command')
      win_native_project_dir = Get-JsonValue $json @('profiles', 'win_native', 'win_native_project_dir')
      win_native_openclaw_command = Get-JsonValue $json @('profiles', 'win_native', 'win_native_openclaw_command')
      win_native_install_command = Get-JsonValue $json @('profiles', 'win_native', 'win_native_install_command')
      dashboard_gateway_root_url = Get-JsonValue $json @('dashboard', 'gateway_root_url')
      legacy_docker_gateway_root_url = Get-JsonValue $json @('docker', 'gateway_root_url')
      network_proxy_mode = Get-JsonValue $json @('network', 'proxy_mode')
    }
  } catch {
    return [pscustomobject]@{
      path = $Path
      exists = $true
      parse_error = $_.Exception.Message
    }
  }
}

function Get-DiffKeys {
  param(
    [object]$Left,
    [object]$Right
  )

  $keys = @(
    'mode_manual_override',
    'mode_last_detected',
    'windows_project_dir',
    'wsl_project_dir',
    'wsl_native_project_dir',
    'wsl_native_openclaw_command',
    'win_native_project_dir',
    'win_native_openclaw_command',
    'win_native_install_command',
    'dashboard_gateway_root_url',
    'legacy_docker_gateway_root_url',
    'network_proxy_mode'
  )

  $diff = @()
  foreach ($key in $keys) {
    $leftProp = $Left.PSObject.Properties[$key]
    $rightProp = $Right.PSObject.Properties[$key]
    $leftValue = if ($null -ne $leftProp) { $leftProp.Value } else { $null }
    $rightValue = if ($null -ne $rightProp) { $rightProp.Value } else { $null }
    if ("$leftValue" -ne "$rightValue") {
      $diff += $key
    }
  }
  return $diff
}

function Convert-WslUncToLinuxPath {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }
  if ($Path -match '^\\\\wsl\$\\[^\\]+\\(.+)$') {
    return '/' + ($Matches[1] -replace '\\', '/')
  }
  if ($Path -match '^/home/') {
    return $Path
  }
  return $null
}

function Get-WslHintFromPath {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) {
    return $null
  }

  if ($Path -match '^\\\\wsl\$\\([^\\]+)\\home\\([^\\]+)\\') {
    return [pscustomobject]@{
      distro = $Matches[1]
      user = $Matches[2]
      source = $Path
      linux_path = Convert-WslUncToLinuxPath $Path
    }
  }

  if ($Path -match '^/home/([^/]+)/') {
    return [pscustomobject]@{
      distro = if ($env:OPENCLAW_WSL_DISTRO) { $env:OPENCLAW_WSL_DISTRO } else { $null }
      user = $Matches[1]
      source = $Path
      linux_path = $Path
    }
  }

  return $null
}

function Get-WslHintFromSettings {
  param([object]$Summary)

  foreach ($candidate in @(
    $Summary.windows_project_dir,
    $Summary.win_native_project_dir,
    $Summary.wsl_native_project_dir,
    $Summary.wsl_project_dir
  )) {
    $hint = Get-WslHintFromPath $candidate
    if ($hint) {
      return $hint
    }
  }

  return $null
}

function Quote-BashLiteral {
  param([string]$Value)
  return "'" + $Value.Replace("'", "'""'""'") + "'"
}

function Resolve-GatewayUrl {
  param([object]$ActiveSettings)

  $raw = $null
  if ($null -ne $ActiveSettings) {
    $raw = $ActiveSettings.dashboard_gateway_root_url
  }
  if ([string]::IsNullOrWhiteSpace($raw)) {
    if ($null -ne $ActiveSettings) {
      $raw = $ActiveSettings.legacy_docker_gateway_root_url
    }
  }
  if ([string]::IsNullOrWhiteSpace($raw)) {
    $raw = 'http://127.0.0.1:18789/'
  }
  return $raw
}

function Get-GatewayPort {
  param([string]$GatewayUrl)

  try {
    $uri = [System.Uri]$GatewayUrl
    if ($uri.Port -gt 0) {
      return $uri.Port
    }
  } catch {
  }
  return 18789
}

function Get-GatewayListenerSummary {
  param([string]$GatewayUrl)

  $port = Get-GatewayPort $GatewayUrl
  $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue)
  if ($listeners.Count -eq 0) {
    return [pscustomobject]@{
      port = $port
      found = $false
    }
  }

  $primary = $listeners | Select-Object -First 1
  $owner = Get-CimInstance Win32_Process -Filter "ProcessId=$($primary.OwningProcess)" -ErrorAction SilentlyContinue

  return [pscustomobject]@{
    port = $port
    found = $true
    listeners = $listeners | Select-Object LocalAddress, LocalPort, OwningProcess
    owner = if ($owner) {
      [pscustomobject]@{
        process_id = $owner.ProcessId
        name = $owner.Name
        command_line = $owner.CommandLine
      }
    } else {
      $null
    }
  }
}

function Invoke-WslCapture {
  param(
    [string]$Distro,
    [string]$User,
    [string]$Command
  )

  $wslExe = Join-Path $env:SystemRoot 'System32\wsl.exe'
  if (-not (Test-Path -LiteralPath $wslExe)) {
    return [pscustomobject]@{
      ExitCode = 127
      Output = ''
      Error = 'wsl.exe not found'
    }
  }

  $invokeArgs = @()
  if (-not [string]::IsNullOrWhiteSpace($Distro)) {
    $invokeArgs += @('-d', $Distro)
  }
  if (-not [string]::IsNullOrWhiteSpace($User)) {
    $invokeArgs += @('-u', $User)
  }
  $invokeArgs += @('--', 'bash', '-lc', $Command)
  $output = & $wslExe @invokeArgs 2>&1
  [pscustomobject]@{
    ExitCode = $LASTEXITCODE
    Output = ($output | Out-String).Trim()
    Error = if ($LASTEXITCODE -eq 0) { $null } else { ($output | Out-String).Trim() }
  }
}

function Get-WslRuntimeSummary {
  param([object]$Hint)

  if ($null -eq $Hint) {
    return $null
  }

  $homeRes = Invoke-WslCapture -Distro $Hint.distro -User $Hint.user -Command 'printf %s "$HOME"'
  if ($homeRes.ExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($homeRes.Output)) {
    return [pscustomobject]@{
      accessible = $false
      distro = $Hint.distro
      user = $Hint.user
      source = $Hint.source
      error = if ($homeRes.Error) { $homeRes.Error } else { 'failed to query WSL home' }
    }
  }

  $home = $homeRes.Output
  $serviceName = if ($env:OPENCLAW_WSL_NATIVE_SERVICE) { $env:OPENCLAW_WSL_NATIVE_SERVICE } else { 'openclaw-gateway.service' }
  $execRes = Invoke-WslCapture -Distro $Hint.distro -User $Hint.user -Command "systemctl --user show $serviceName --property ExecStart --value 2>/dev/null || true"
  $execStart = $execRes.Output

  $projectDir = $null
  if ($execStart -match '(/[^ ;]+?)/dist/index\.js') {
    $projectDir = $Matches[1]
  }
  if ([string]::IsNullOrWhiteSpace($projectDir) -and $Hint.linux_path) {
    $projectDir = $Hint.linux_path
  }
  if ([string]::IsNullOrWhiteSpace($projectDir)) {
    foreach ($candidate in @(
      "$home/src/openclaw-cn",
      "$home/src/openclaw",
      "$home/openclaw-cn",
      "$home/openclaw"
    )) {
      $quoted = Quote-BashLiteral $candidate
      $check = Invoke-WslCapture -Distro $Hint.distro -User $Hint.user -Command "if [ -d $quoted ]; then printf %s $quoted; fi"
      if (-not [string]::IsNullOrWhiteSpace($check.Output)) {
        $projectDir = $check.Output
        break
      }
    }
  }

  $dataDir = "$home/.openclaw"
  $configPath = "$dataDir/openclaw.json"
  $sessionsPath = "$dataDir/agents/main/sessions/sessions.json"
  $configExists = (Invoke-WslCapture -Distro $Hint.distro -User $Hint.user -Command "if [ -f $(Quote-BashLiteral $configPath) ]; then printf yes; else printf no; fi").Output
  $sessionsExists = (Invoke-WslCapture -Distro $Hint.distro -User $Hint.user -Command "if [ -f $(Quote-BashLiteral $sessionsPath) ]; then printf yes; else printf no; fi").Output

  return [pscustomobject]@{
    accessible = $true
    distro = if ([string]::IsNullOrWhiteSpace($Hint.distro)) { 'default' } else { $Hint.distro }
    user = $Hint.user
    source = $Hint.source
    home = $home
    project_dir = if ([string]::IsNullOrWhiteSpace($projectDir)) { $null } else { $projectDir }
    data_dir = $dataDir
    config_path = $configPath
    config_exists = ($configExists -eq 'yes')
    sessions_path = $sessionsPath
    sessions_exists = ($sessionsExists -eq 'yes')
    service_name = $serviceName
    exec_start = $execStart
  }
}

$packageDir = $PSScriptRoot
$packageExe = Join-Path $packageDir 'openclaw-control-panel.exe'
$packageSettings = Join-Path $packageDir 'openclaw-control-panel-settings.json'

$observedActiveCandidates = @(
  'E:\OPC\panel\openclaw-control-panel-settings.json',
  'E:\openclaw-windows-panel-main\openclaw-control-panel-settings.json'
) | Select-Object -Unique

$settingsCandidates = @($packageSettings) + $observedActiveCandidates | Select-Object -Unique
$settingsSummaries = @($settingsCandidates | ForEach-Object { Read-SettingsSummary $_ })
$activeSettingsCandidates = @($settingsSummaries | Where-Object { $_.exists -and $_.path -ne $packageSettings })
$activeSettings = $activeSettingsCandidates | Select-Object -First 1
$packageSettingsSummary = $settingsSummaries | Where-Object { $_.path -eq $packageSettings } | Select-Object -First 1

$gatewayUrl = Resolve-GatewayUrl $activeSettings
$listenerSummary = Get-GatewayListenerSummary $gatewayUrl
$wslHint = Get-WslHintFromSettings $activeSettings
$wslRuntime = Get-WslRuntimeSummary $wslHint

$notes = New-Object System.Collections.Generic.List[string]

if ($activeSettings) {
  $notes.Add("Observed active settings file: $($activeSettings.path)")
  if ($activeSettings.path -ne $packageSettings) {
    $notes.Add('Do not assume the EXE-directory settings file is the one currently in use.')
  }
} else {
  $notes.Add('No observed active settings file was found in the known runtime location list.')
}

if ($packageSettingsSummary.exists -and $activeSettings) {
  $diffKeys = Get-DiffKeys $packageSettingsSummary $activeSettings
  if ($diffKeys.Count -gt 0) {
    $notes.Add('Package settings and active settings differ: ' + ($diffKeys -join ', '))
  }
}

if ($listenerSummary.found -and $listenerSummary.owner -and $listenerSummary.owner.name -eq 'wslrelay.exe') {
  $notes.Add("Gateway port $($listenerSummary.port) is owned by wslrelay.exe, so the live OpenClaw runtime is WSL-backed.")
}

if ($wslRuntime -and $wslRuntime.accessible) {
  $notes.Add("Live runtime config path: $($wslRuntime.config_path)")
  if ($wslRuntime.project_dir) {
    $notes.Add("Live runtime project dir: $($wslRuntime.project_dir)")
  }
  $notes.Add("Live runtime session store: $($wslRuntime.sessions_path)")
} elseif ($wslRuntime -and -not $wslRuntime.accessible) {
  $notes.Add('WSL runtime path detection failed: ' + $wslRuntime.error)
}

if ($activeSettings -and -not [string]::IsNullOrWhiteSpace($activeSettings.win_native_project_dir)) {
  $notes.Add('In current panel builds, the startup banner "项目目录 / Project dir" may still be driven by profiles.wsl_docker.windows_project_dir rather than profiles.win_native.win_native_project_dir.')
}

if ($activeSettings) {
  if (-not [string]::IsNullOrWhiteSpace($activeSettings.legacy_docker_gateway_root_url) -and [string]::IsNullOrWhiteSpace($activeSettings.dashboard_gateway_root_url)) {
    $notes.Add('gateway_root_url appears only under docker.*; current builds expect dashboard.gateway_root_url.')
  }
}

$report = [pscustomobject]@{
  package_dir = $packageDir
  panel_exe_exists = Test-Path -LiteralPath $packageExe
  panel_exe = $packageExe
  package_settings = $packageSettingsSummary
  observed_active_settings = $activeSettings
  all_active_settings_candidates = $activeSettingsCandidates
  all_settings_candidates = $settingsSummaries
  gateway_url = $gatewayUrl
  gateway_listener = $listenerSummary
  wsl_hint = $wslHint
  wsl_runtime = $wslRuntime
  notes = @($notes)
}

if ($AsJson) {
  $report | ConvertTo-Json -Depth 10
  exit 0
}

Write-Host ('Package dir:       ' + $report.package_dir)
Write-Host ('Panel EXE:         ' + $report.panel_exe)
Write-Host ('EXE exists:        ' + ($(if ($report.panel_exe_exists) { 'yes' } else { 'no' })))
Write-Host ('Gateway URL:       ' + $report.gateway_url)
Write-Host ''

foreach ($summary in $settingsSummaries) {
  Write-Host ('Settings:          ' + $summary.path)
  Write-Host ('Exists:            ' + ($(if ($summary.exists) { 'yes' } else { 'no' })))
  if ($summary.PSObject.Properties['parse_error']) {
    Write-Host ('Parse error:       ' + $summary.parse_error)
  } elseif ($summary.exists) {
    Write-Host ('Mode:              ' + $summary.mode_manual_override)
    Write-Host ('Banner dir:        ' + $summary.windows_project_dir)
    Write-Host ('WSL native dir:    ' + $summary.wsl_native_project_dir)
    Write-Host ('Win native dir:    ' + $summary.win_native_project_dir)
    Write-Host ('Gateway URL:       ' + $summary.dashboard_gateway_root_url)
    Write-Host ('Proxy mode:        ' + $summary.network_proxy_mode)
  }
  Write-Host ''
}

Write-Host 'Gateway listener:'
if ($listenerSummary.found) {
  foreach ($listener in $listenerSummary.listeners) {
    Write-Host ('- ' + $listener.LocalAddress + ':' + $listener.LocalPort + ' pid=' + $listener.OwningProcess)
  }
  if ($listenerSummary.owner) {
    Write-Host ('- owner: ' + $listenerSummary.owner.name)
    if ($listenerSummary.owner.command_line) {
      Write-Host ('- cmd:   ' + $listenerSummary.owner.command_line)
    }
  }
} else {
  Write-Host ('- no listener on port ' + $listenerSummary.port)
}
Write-Host ''

Write-Host 'WSL runtime:'
if ($wslRuntime -and $wslRuntime.accessible) {
  Write-Host ('- distro:          ' + $wslRuntime.distro)
  Write-Host ('- user:            ' + $wslRuntime.user)
  Write-Host ('- source hint:     ' + $wslRuntime.source)
  Write-Host ('- project dir:     ' + $wslRuntime.project_dir)
  Write-Host ('- data dir:        ' + $wslRuntime.data_dir)
  Write-Host ('- config path:     ' + $wslRuntime.config_path)
  Write-Host ('- config exists:   ' + ($(if ($wslRuntime.config_exists) { 'yes' } else { 'no' })))
  Write-Host ('- sessions path:   ' + $wslRuntime.sessions_path)
  Write-Host ('- sessions exists: ' + ($(if ($wslRuntime.sessions_exists) { 'yes' } else { 'no' })))
  if ($wslRuntime.exec_start) {
    Write-Host ('- exec start:      ' + $wslRuntime.exec_start)
  }
} elseif ($wslRuntime) {
  Write-Host ('- failed: ' + $wslRuntime.error)
} else {
  Write-Host '- no WSL runtime hint could be derived from the current settings'
}
Write-Host ''

Write-Host 'Notes:'
foreach ($note in $notes) {
  Write-Host ('- ' + $note)
}
