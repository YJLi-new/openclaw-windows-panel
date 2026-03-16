[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$wslExe = Join-Path $env:SystemRoot 'System32\wsl.exe'
$distro = if ($env:OPENCLAW_WSL_DISTRO) { $env:OPENCLAW_WSL_DISTRO.Trim() } else { 'Ubuntu-24.04' }
$linuxUser = if ($env:OPENCLAW_WSL_USER) { $env:OPENCLAW_WSL_USER.Trim() } else { 'administrator' }
$linuxHome = if ($env:OPENCLAW_WSL_HOME) { $env:OPENCLAW_WSL_HOME.Trim() } else { '/home/' + $linuxUser }
$openclawPath = if ($env:OPENCLAW_WSL_OPENCLAW_PATH) { $env:OPENCLAW_WSL_OPENCLAW_PATH.Trim() } else { '/usr/local/bin/openclaw' }
$gatewayPort = if ($env:OPENCLAW_GATEWAY_PORT) { $env:OPENCLAW_GATEWAY_PORT.Trim() } else { '18790' }
$gatewayBind = if ($env:OPENCLAW_GATEWAY_BIND) { $env:OPENCLAW_GATEWAY_BIND.Trim() } else { 'loopback' }
$linuxCommand = 'export HOME="{0}" USER="{1}" LOGNAME="{1}"; cd "{0}" && exec "{2}" gateway run --port {3} --bind {4}' -f $linuxHome, $linuxUser, $openclawPath, $gatewayPort, $gatewayBind

if (-not (Test-Path $wslExe)) {
  throw "wsl.exe was not found at $wslExe"
}

& $wslExe -d $distro -u $linuxUser -- bash -lc $linuxCommand
exit $LASTEXITCODE
