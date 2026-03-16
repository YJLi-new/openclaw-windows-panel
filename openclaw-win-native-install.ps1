$ErrorActionPreference = 'Stop'
$entry = Join-Path $PSScriptRoot 'openclaw-win-native-entry.ps1'

& powershell.exe -NoProfile -ExecutionPolicy Bypass -File $entry start
exit $LASTEXITCODE
