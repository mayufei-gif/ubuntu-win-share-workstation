[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$ClientExe,

  [Parameter(Mandatory = $true)]
  [string]$Share,

  [Parameter(Mandatory = $true)]
  [string]$NasHost,

  [Parameter(Mandatory = $true)]
  [string]$NasUser,

  [string]$NasRemoteRoot = "Win7Uploads",

  [string]$NasSshTarget = "",

  [ValidateRange(1, 10)]
  [int]$Rounds = 2,

  [ValidateRange(30, 1800)]
  [int]$TimeoutSeconds = 300,

  [string]$OutputPath = "artifacts\win7-e2e-current.json"
)

$ErrorActionPreference = "Stop"

function Quote-ProcessArgument {
  param([string]$Value)
  if ($null -eq $Value) {
    return '""'
  }
  return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-BoundedClient {
  param(
    [string[]]$Arguments,
    [int]$Timeout
  )

  $start = New-Object Diagnostics.ProcessStartInfo
  $start.FileName = $script:resolvedClient
  $start.Arguments = (
    $Arguments | ForEach-Object { Quote-ProcessArgument $_ }
  ) -join " "
  $start.UseShellExecute = $false
  $start.CreateNoWindow = $true
  $start.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden

  $process = New-Object Diagnostics.Process
  $process.StartInfo = $start
  [void]$process.Start()
  if (-not $process.WaitForExit($Timeout * 1000)) {
    try {
      & "$env:SystemRoot\System32\taskkill.exe" `
        /PID $process.Id /T /F | Out-Null
    }
    catch {
    }
    throw "Client command timed out: $($Arguments -join ' ')"
  }
  if ($process.ExitCode -ne 0) {
    throw "Client command failed with exit code $($process.ExitCode): $($Arguments -join ' ')"
  }
}

function Invoke-SyncProbeWithRetry {
  param(
    [string]$Source,
    [string]$Target,
    [string]$ResultPath,
    [int]$Timeout,
    [int]$MaxAttempts = 2
  )

  $attempts = @()
  for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    if (Test-Path -LiteralPath $ResultPath) {
      Remove-Item -LiteralPath $ResultPath -Force
    }

    $watch = [Diagnostics.Stopwatch]::StartNew()
    $errorText = ""
    try {
      Invoke-BoundedClient -Timeout $Timeout -Arguments @(
        "--sync-test",
        "--source", $Source,
        "--target", $Target,
        "--result", $ResultPath
      )
    }
    catch {
      $errorText = $_.Exception.Message
    }
    finally {
      $watch.Stop()
    }

    $resultText = if (Test-Path -LiteralPath $ResultPath) {
      (Get-Content -LiteralPath $ResultPath -Raw).Trim()
    } else {
      ""
    }
    $success =
      [string]::IsNullOrWhiteSpace($errorText) -and
      $resultText -eq "WIN7_CLIENT_SYNC_PROBE_OK"
    $attempts += [ordered]@{
      Attempt = $attempt
      ElapsedMilliseconds = $watch.ElapsedMilliseconds
      Success = $success
      Result = $resultText
      Error = $errorText
    }
    if ($success) {
      return [pscustomobject]@{
        Success = $true
        Result = $resultText
        Attempts = $attempts
        Failure = ""
      }
    }
    if ($attempt -lt $MaxAttempts) {
      Start-Sleep -Seconds 5
    }
  }

  $last = $attempts[$attempts.Count - 1]
  $failure =
    "Sync probe failed after $MaxAttempts attempts. " +
    "result=$($last.Result) error=$($last.Error)"
  return [pscustomobject]@{
    Success = $false
    Result = $last.Result
    Attempts = $attempts
    Failure = $failure
  }
}

function Read-SafeXml {
  param([string]$Path)

  $settings = New-Object System.Xml.XmlReaderSettings
  $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
  $settings.XmlResolver = $null
  $reader = $null
  try {
    $reader = [System.Xml.XmlReader]::Create($Path, $settings)
    $document = New-Object System.Xml.XmlDocument
    $document.XmlResolver = $null
    $document.Load($reader)
    return $document
  }
  finally {
    if ($reader) {
      $reader.Dispose()
    }
  }
}

function Wait-UploadResult {
  param(
    [string]$Path,
    [int]$Timeout
  )

  $deadline = (Get-Date).AddSeconds($Timeout)
  do {
    if (Test-Path -LiteralPath $Path) {
      try {
        $document = Read-SafeXml -Path $Path
        $status = [string]$document.NasUploadResult.Status
        if ($status -eq "success") {
          return $document.NasUploadResult
        }
        if ($status -eq "failed") {
          throw "NAS upload failed: $($document.NasUploadResult.Message)"
        }
      }
      catch [System.IO.IOException] {
      }
      catch [System.Xml.XmlException] {
      }
    }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)

  throw "Timed out waiting for NAS upload result: $Path"
}

function Get-NasFile {
  param(
    [string]$Profile,
    [string]$FileName,
    [int]$Timeout
  )

  if ($NasRemoteRoot -notmatch '^[A-Za-z0-9._/-]+$') {
    throw "NasRemoteRoot contains unsupported characters."
  }
  if ($Profile -notmatch '^[A-Za-z0-9._-]+$' -or
      $FileName -notmatch '^[A-Za-z0-9._-]+$') {
    throw "Profile or file name is unsafe for the NAS verification command."
  }

  $remoteCommand =
    "find `"`$HOME/$NasRemoteRoot`" -type f " +
    "-path '*/$Profile/$FileName' -exec sha256sum {} \; -quit"
  $deadline = (Get-Date).AddSeconds($Timeout)
  do {
    $lines = @(
      ssh `
        -o BatchMode=yes `
        -o ConnectTimeout=8 `
        -o ServerAliveInterval=5 `
        -o ServerAliveCountMax=2 `
        $script:resolvedNasSshTarget `
        $remoteCommand 2>$null
    )
    $exitCode = $LASTEXITCODE
    $line = $lines |
      Where-Object { $_ -match '^[0-9a-fA-F]{64}\s+.+' } |
      Select-Object -First 1
    if ($exitCode -eq 0 -and
        $line -match '^([0-9a-fA-F]{64})\s+(.+)$') {
      return [pscustomobject]@{
        Sha256 = $Matches[1].ToLowerInvariant()
        Path = $Matches[2]
      }
    }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)

  throw "NAS verification file was not found for profile $Profile."
}

$resolvedClient = (Resolve-Path -LiteralPath $ClientExe).Path
$resolvedNasSshTarget = if ([string]::IsNullOrWhiteSpace($NasSshTarget)) {
  "$NasUser@$NasHost"
} else {
  $NasSshTarget
}
$shareRoot = $Share.TrimEnd("\")
if (-not (Test-Path -LiteralPath $shareRoot)) {
  throw "Ubuntu share is unavailable: $shareRoot"
}

$profile = "CodexE2E-" + (Get-Date -Format "yyyyMMdd-HHmmss")
$source = Join-Path $env:TEMP (
  "UbuntuWinShare-E2E-" + [Guid]::NewGuid().ToString("N"))
$target = Join-Path (
  Join-Path (
    Join-Path $shareRoot "Win7Sync"
  ) $env:COMPUTERNAME
) $profile
$output = if ([IO.Path]::IsPathRooted($OutputPath)) {
  $OutputPath
} else {
  Join-Path (Get-Location).Path $OutputPath
}
$outputDirectory = Split-Path -Parent $output
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$runEvidenceDirectory = Join-Path (
  Join-Path $outputDirectory "win7-e2e-evidence"
) $profile
New-Item -ItemType Directory -Force -Path $runEvidenceDirectory | Out-Null

$evidence = [ordered]@{
  Profile = $profile
  Machine = $env:COMPUTERNAME
  ClientExe = $resolvedClient
  ClientSha256 = (
    Get-FileHash -LiteralPath $resolvedClient -Algorithm SHA256
  ).Hash.ToLowerInvariant()
  Source = $source
  Target = $target
  NasSshTarget = $resolvedNasSshTarget
  NasRemoteRoot = $NasRemoteRoot
  StartedUtc = [DateTime]::UtcNow.ToString("o")
  EvidenceDirectory = $runEvidenceDirectory
  Rounds = @()
}

try {
  New-Item -ItemType Directory -Force -Path $source | Out-Null

  for ($round = 1; $round -le $Rounds; $round++) {
    $payload = @(
      "profile=$profile"
      "round=$round"
      "created=$(Get-Date -Format o)"
      "machine=$env:COMPUTERNAME"
    ) -join "`r`n"
    $payloadPath = Join-Path $source "customer-e2e.txt"
    [IO.File]::WriteAllText(
      $payloadPath,
      $payload + "`r`n",
      [Text.UTF8Encoding]::new($false))
    if ($round -gt 1) {
      $nested = Join-Path $source "nested"
      New-Item -ItemType Directory -Force -Path $nested | Out-Null
      [IO.File]::WriteAllText(
        (Join-Path $nested "round-$round.txt"),
        "round=$round`r`n",
        [Text.UTF8Encoding]::new($false))
    }

    $syncResult = Join-Path (
      $runEvidenceDirectory
    ) "sync-result-$round.txt"
    $syncProbe = Invoke-SyncProbeWithRetry `
      -Source $source `
      -Target $target `
      -ResultPath $syncResult `
      -Timeout $TimeoutSeconds
    if (-not $syncProbe.Success) {
      $evidence["FailedRound"] = $round
      $evidence["FailedSyncAttempts"] = $syncProbe.Attempts
      throw $syncProbe.Failure
    }
    $syncText = $syncProbe.Result

    $queueResult = Join-Path (
      $runEvidenceDirectory
    ) "queue-result-$round.txt"
    Invoke-BoundedClient -Timeout $TimeoutSeconds -Arguments @(
      "--queue-test",
      "--share", $shareRoot,
      "--profile", $profile,
      "--nas-host", $NasHost,
      "--nas-user", $NasUser,
      "--nas-remote", $NasRemoteRoot,
      "--result", $queueResult
    )
    $queueText = (Get-Content -LiteralPath $queueResult -Raw).Trim()
    if ($queueText -notmatch
        '^WIN7_CLIENT_QUEUE_PROBE_OK job_id=([0-9a-f]{32})$') {
      throw "Unexpected queue probe result: $queueText"
    }
    $jobId = $Matches[1]
    $resultXml = Join-Path (
      Join-Path (
        Join-Path $shareRoot ".ubuntu-win-share\jobs"
      ) "results"
    ) "codexacceptance-$profile-$jobId.result.xml"
    $upload = Wait-UploadResult `
      -Path $resultXml `
      -Timeout $TimeoutSeconds

    $sharePayload = Join-Path $target "customer-e2e.txt"
    $localHash = (
      Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $shareHash = (
      Get-FileHash -LiteralPath $sharePayload -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    $nasFile = Get-NasFile `
      -Profile $profile `
      -FileName "customer-e2e.txt" `
      -Timeout ([Math]::Min($TimeoutSeconds, 120))

    if ($localHash -ne $shareHash -or
        $localHash -ne $nasFile.Sha256) {
      throw (
        "SHA-256 mismatch in round $round. " +
        "local=$localHash share=$shareHash nas=$($nasFile.Sha256)")
    }

    $evidence.Rounds += [ordered]@{
      Round = $round
      JobId = $jobId
      SyncResult = $syncText
      QueueResult = $queueText
      Status = [string]$upload.Status
      UploadedFiles = [int]$upload.UploadedFiles
      UploadedBytes = [long]$upload.UploadedBytes
      NasKeyInstalled = [string]$upload.NasKeyInstalled
      LocalSha256 = $localHash
      ShareSha256 = $shareHash
      NasSha256 = $nasFile.Sha256
      NasPath = $nasFile.Path
      ResultXml = $resultXml
      SyncAttempts = $syncProbe.Attempts
    }
  }

  $evidence["CompletedUtc"] = [DateTime]::UtcNow.ToString("o")
  [IO.File]::WriteAllText(
    $output,
    ($evidence | ConvertTo-Json -Depth 8) + "`n",
    [Text.UTF8Encoding]::new($false))
  Write-Host "WIN7_SAME_NAS_E2E_OK output=$output"
}
catch {
  $evidence["FailedUtc"] = [DateTime]::UtcNow.ToString("o")
  $evidence["Failure"] = $_.Exception.Message
  [IO.File]::WriteAllText(
    $output,
    ($evidence | ConvertTo-Json -Depth 8) + "`n",
    [Text.UTF8Encoding]::new($false))
  throw
}
finally {
  if (Test-Path -LiteralPath $source) {
    Remove-Item -LiteralPath $source -Recurse -Force
  }
}
