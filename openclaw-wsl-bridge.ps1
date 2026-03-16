[CmdletBinding()]
param(
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$Rest
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$taskName = if ($env:OPENCLAW_WSL_BRIDGE_TASK) { $env:OPENCLAW_WSL_BRIDGE_TASK.Trim() } else { 'OpenClawWslCommandV2' }
$stageDir = if ($env:OPENCLAW_WSL_BRIDGE_DIR) { $env:OPENCLAW_WSL_BRIDGE_DIR.Trim() } else { Join-Path $env:LOCALAPPDATA 'OpenClawWslBridge' }
$requestFile = Join-Path $stageDir 'openclaw-request-v2.json'
$linuxUser = if ($env:OPENCLAW_WSL_USER) { $env:OPENCLAW_WSL_USER.Trim() } else { 'administrator' }
$linuxHome = if ($env:OPENCLAW_WSL_HOME) { $env:OPENCLAW_WSL_HOME.Trim() } else { '/home/' + $linuxUser }
$openclawPath = if ($env:OPENCLAW_WSL_OPENCLAW_PATH) { $env:OPENCLAW_WSL_OPENCLAW_PATH.Trim() } else { '/usr/local/bin/openclaw' }
$runId = [guid]::NewGuid().ToString()
$runDir = Join-Path (Join-Path $stageDir 'runs') $runId
$stdoutFile = Join-Path $runDir 'stdout.log'
$stderrFile = Join-Path $runDir 'stderr.log'
$metaFile = Join-Path $runDir 'meta.json'
$mutex = New-Object System.Threading.Mutex($false, 'Global\OpenClawWslBridgeMutex')
$lockTaken = $false

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

function ConvertTo-BashSingleQuoted {
  param([Parameter(Mandatory = $true)][string]$Value)

  $replacement = ([string][char]39) + ([string][char]34) + ([string][char]39) + ([string][char]34) + ([string][char]39)
  return "'" + $Value.Replace("'", $replacement) + "'"
}

function Invoke-BridgeFailure {
  param([Parameter(Mandatory = $true)][string]$Message)
  throw $Message
}

try {
  $lockTaken = $mutex.WaitOne([TimeSpan]::FromMinutes(30))
  if (-not $lockTaken) {
    Invoke-BridgeFailure "Timed out waiting for the OpenClaw WSL bridge mutex."
  }

  $linuxCommand = 'export HOME=' + (ConvertTo-BashSingleQuoted $linuxHome) +
    ' USER=' + (ConvertTo-BashSingleQuoted $linuxUser) +
    ' LOGNAME=' + (ConvertTo-BashSingleQuoted $linuxUser) +
    '; cd ' + (ConvertTo-BashSingleQuoted $linuxHome) +
    ' && ' + (ConvertTo-BashSingleQuoted $openclawPath)

  if ($Rest -and $Rest.Count -gt 0) {
    $linuxCommand += ' ' + (($Rest | ForEach-Object { ConvertTo-BashSingleQuoted $_ }) -join ' ')
  }

  $request = [ordered]@{
    runId = $runId
    linuxCommand = $linuxCommand
    stdoutFile = $stdoutFile
    stderrFile = $stderrFile
    metaFile = $metaFile
  }

  Write-Utf8File -Path $requestFile -Content (($request | ConvertTo-Json -Depth 5) + "`n")

  if (-not (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue)) {
    Invoke-BridgeFailure "Scheduled task '$taskName' was not found. Run install-openclaw-wsl-bridge.ps1 first."
  }

  Start-ScheduledTask -TaskName $taskName

  $deadline = (Get-Date).AddMinutes(30)
  $meta = $null
  do {
    Start-Sleep -Milliseconds 750
    if (Test-Path $metaFile) {
      try {
        $meta = Get-Content -Path $metaFile -Raw -Encoding UTF8 | ConvertFrom-Json
      }
      catch {
        $meta = $null
      }
    }
  } while (($null -eq $meta -or $meta.status -eq 'starting') -and (Get-Date) -lt $deadline)

  if ($null -eq $meta) {
    Invoke-BridgeFailure "Timed out waiting for bridge result metadata at $metaFile"
  }

  if (Test-Path $stdoutFile) {
    $stdout = Get-Content -Path $stdoutFile -Raw -Encoding UTF8
    if ($stdout) {
      Write-Output $stdout.TrimEnd("`r", "`n")
    }
  }

  if (Test-Path $stderrFile) {
    $stderr = Get-Content -Path $stderrFile -Raw -Encoding UTF8
    if ($stderr) {
      [Console]::Error.Write($stderr)
    }
  }

  if ($meta.status -eq 'failed') {
    exit 1
  }

  exit [int]$meta.exitCode
}
finally {
  if ($lockTaken) {
    $mutex.ReleaseMutex() | Out-Null
  }
  $mutex.Dispose()
}
