[CmdletBinding()]
param(
  [string]$OutputPath = $(Join-Path $PSScriptRoot 'openclaw-control-panel-settings.json'),
  [string]$Distro = 'Ubuntu-24.04',
  [string]$LinuxUser = 'administrator',
  [string]$GatewayRootUrl = 'http://127.0.0.1:18790/',
  [string]$WslProjectDir = ''
)

$ErrorActionPreference = 'Stop'

if (-not $GatewayRootUrl.EndsWith('/')) {
  $GatewayRootUrl += '/'
}

if ([string]::IsNullOrWhiteSpace($WslProjectDir)) {
  $WslProjectDir = '\\wsl$\' + $Distro + '\opt\openclaw-zh'
}

$settings = [ordered]@{
  schema_version = 2
  language = 'zh-Hans'
  theme = 'system'
  mode = [ordered]@{
    auto_detect = $false
    manual_override = 'win_native'
    last_detected = 'win_native'
  }
  profiles = [ordered]@{
    wsl_docker = [ordered]@{
      windows_project_dir = ''
      wsl_project_dir = ''
      wsl_docker_openclaw_dir = ''
      wsl_docker_data_dir = ''
      wsl_docker_start_script = ''
      wsl_docker_dashboard_script = ''
    }
    wsl_native = [ordered]@{
      wsl_native_project_dir = '/opt/openclaw-zh'
      wsl_native_openclaw_command = ''
      wsl_native_install_command = ''
    }
    win_docker = [ordered]@{
      win_docker_openclaw_dir = ''
      win_docker_data_dir = ''
    }
    win_native = [ordered]@{
      win_native_project_dir = $WslProjectDir
      win_native_openclaw_command = (Join-Path $PSScriptRoot 'openclaw-win-admin-wsl-entry.ps1')
      win_native_install_command = (Join-Path $PSScriptRoot 'openclaw-win-admin-wsl-install.ps1')
    }
  }
  docker = [ordered]@{
    docker_compose_command = ''
    gateway_service_name = ''
  }
  dashboard = [ordered]@{
    gateway_root_url = $GatewayRootUrl
    browser_target = 'system'
    wsl_chrome_backend = ''
  }
  network = [ordered]@{
    proxy_mode = 'off'
    custom_http_proxy = ''
    custom_https_proxy = ''
    custom_all_proxy = ''
    custom_no_proxy = 'localhost,127.0.0.1,::1'
    wsl_sudo_password_dpapi = ''
  }
}

$json = $settings | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText($OutputPath, $json + "`r`n", [System.Text.UTF8Encoding]::new($false))

[PSCustomObject]@{
  output = $OutputPath
  distro = $Distro
  linux_user = $LinuxUser
  gateway_root_url = $GatewayRootUrl
  wsl_project_dir = $WslProjectDir
} | ConvertTo-Json -Depth 4
