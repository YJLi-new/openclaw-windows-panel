[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$bridgeInstall = Join-Path $PSScriptRoot 'install-openclaw-wsl-bridge.ps1'
$entryScript = Join-Path $PSScriptRoot 'openclaw-win-admin-wsl-entry.ps1'

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $bridgeInstall
if ($LASTEXITCODE -ne 0) {
  exit $LASTEXITCODE
}

& powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $entryScript start
exit $LASTEXITCODE
