[CmdletBinding()]
param(
  [string]$Distro = $(if ($env:OPENCLAW_WSL_DISTRO) { $env:OPENCLAW_WSL_DISTRO } else { 'Ubuntu-24.04' }),
  [string]$LinuxUser = $(if ($env:OPENCLAW_WSL_USER) { $env:OPENCLAW_WSL_USER } else { 'administrator' }),
  [string]$RequestFile = $(Join-Path $(Join-Path $env:LOCALAPPDATA 'OpenClawWslBridge') 'openclaw-request-v2.json')
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Write-Utf8File {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Content
  )

  $directory = Split-Path -Parent $Path
  if ($directory) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
  }

  [IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Write-Meta {
  param(
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][hashtable]$Payload
  )

  Write-Utf8File -Path $Path -Content (($Payload | ConvertTo-Json -Depth 5) + "`n")
}

function Quote-CliArg {
  param([Parameter(Mandatory = $true)][string]$Value)

  if ($Value -match '[\s"]') {
    return '"' + $Value.Replace('"', '\"') + '"'
  }

  return $Value
}

$metaFile = $null
$stderrFile = $null
$runId = ''

try {
  $wslExe = Join-Path $env:SystemRoot 'System32\wsl.exe'
  if (-not (Test-Path $wslExe)) {
    throw "wsl.exe was not found at $wslExe"
  }

  if (-not (Test-Path $RequestFile)) {
    throw "Request file not found: $RequestFile"
  }

  $request = Get-Content -Path $RequestFile -Raw -Encoding UTF8 | ConvertFrom-Json
  $runId = [string]$request.runId
  $linuxCommand = [string]$request.linuxCommand
  $stdoutFile = [string]$request.stdoutFile
  $stderrFile = [string]$request.stderrFile
  $metaFile = [string]$request.metaFile

  if ([string]::IsNullOrWhiteSpace($linuxCommand)) {
    throw "linuxCommand is empty in $RequestFile"
  }

  Write-Utf8File -Path $stdoutFile -Content ''
  Write-Utf8File -Path $stderrFile -Content ''
  Write-Meta -Path $metaFile -Payload @{
    runId = $runId
    status = 'starting'
    startedAt = [DateTime]::UtcNow.ToString('o')
    linuxCommand = $linuxCommand
    distro = $Distro
    linuxUser = $LinuxUser
  }

  $argumentParts = @()
  if (-not [string]::IsNullOrWhiteSpace($Distro)) {
    $argumentParts += @('-d', $Distro)
  }
  if (-not [string]::IsNullOrWhiteSpace($LinuxUser)) {
    $argumentParts += @('-u', $LinuxUser)
  }

  $psi = New-Object System.Diagnostics.ProcessStartInfo
  $psi.FileName = $wslExe
  $psi.Arguments = (($argumentParts | ForEach-Object { Quote-CliArg $_ }) -join ' ')
  $psi.UseShellExecute = $false
  $psi.RedirectStandardInput = $true
  $psi.RedirectStandardOutput = $true
  $psi.RedirectStandardError = $true
  $psi.CreateNoWindow = $true

  $proc = New-Object System.Diagnostics.Process
  $proc.StartInfo = $psi
  $proc.Start() | Out-Null

  $proc.StandardInput.NewLine = "`n"
  $proc.StandardInput.WriteLine($linuxCommand)
  $proc.StandardInput.WriteLine('exit')
  $proc.StandardInput.Flush()
  $proc.StandardInput.Close()

  $stdout = $proc.StandardOutput.ReadToEnd()
  $stderr = $proc.StandardError.ReadToEnd()
  $proc.WaitForExit()

  Write-Utf8File -Path $stdoutFile -Content $stdout
  Write-Utf8File -Path $stderrFile -Content $stderr
  Write-Meta -Path $metaFile -Payload @{
    runId = $runId
    status = 'completed'
    finishedAt = [DateTime]::UtcNow.ToString('o')
    exitCode = $proc.ExitCode
    stdoutFile = $stdoutFile
    stderrFile = $stderrFile
    distro = $Distro
    linuxUser = $LinuxUser
  }

  exit $proc.ExitCode
}
catch {
  $message = $_ | Out-String

  if ($stderrFile) {
    Write-Utf8File -Path $stderrFile -Content $message
  }

  if ($metaFile) {
    Write-Meta -Path $metaFile -Payload @{
      runId = $runId
      status = 'failed'
      finishedAt = [DateTime]::UtcNow.ToString('o')
      exitCode = 1
      error = $message
      distro = $Distro
      linuxUser = $LinuxUser
    }
  }

  exit 1
}
