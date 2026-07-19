[CmdletBinding()]
param(
  [string]$DriveLetter = "I",

  [string]$SshHost,

  [string]$RemotePath,

  [string]$ProbePrefix = "_ubuntu_win_share_probe"
)

$ErrorActionPreference = "Stop"

function Normalize-DriveName {
  param([string]$Value)
  return $Value.Trim().TrimEnd(":").ToUpperInvariant()
}

function Get-Sha256 {
  param([string]$Path)
  return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$driveName = Normalize-DriveName $DriveLetter
$root = "$driveName`:\"
if (-not (Test-Path -LiteralPath $root)) {
  throw "Drive $driveName`: is not available."
}

$stamp = Get-Date -Format "yyyyMMdd_HHmmss"
$probeName = "$ProbePrefix`_$stamp.txt"
$probePath = Join-Path $root $probeName

@(
  "probe=$stamp",
  "source=windows",
  "phase=initial"
) | Set-Content -LiteralPath $probePath -Encoding UTF8

Start-Sleep -Milliseconds 500

@(
  "probe=$stamp",
  "source=windows",
  "phase=modified",
  "tick=$(Get-Date -Format o)"
) | Set-Content -LiteralPath $probePath -Encoding UTF8

$localHash = Get-Sha256 $probePath
Write-Host "Windows file: $probePath"
Write-Host "Windows sha256: $localHash"

if ($SshHost -and $RemotePath) {
  $remoteFile = ($RemotePath.TrimEnd("/") + "/" + $probeName)
  $remoteCommand = "set -e; test -f '$remoteFile'; sha256sum '$remoteFile'; sed -n '1,8p' '$remoteFile'"
  $remoteOutput = ssh -o BatchMode=yes $SshHost $remoteCommand
  $remoteHash = (($remoteOutput | Select-Object -First 1) -split "\s+")[0].ToLowerInvariant()
  Write-Host "Ubuntu file: $remoteFile"
  Write-Host "Ubuntu sha256: $remoteHash"
  if ($remoteHash -ne $localHash) {
    throw "Hash mismatch. Windows=$localHash Ubuntu=$remoteHash"
  }
}

Write-Host "SYNC_OK"
