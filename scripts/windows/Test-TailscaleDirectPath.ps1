[CmdletBinding()]
param(
  [string]$Peer = "100.95.140.72",
  [string]$DriveLetter = "Z",
  [string]$ExpectedShare = "\\100.95.140.72\ubuntu-win",
  [int]$PingCount = 5,
  [int]$PingTimeoutSeconds = 4,
  [int]$CommandTimeoutSeconds = 15,
  [switch]$RequireDirect
)

$ErrorActionPreference = "Stop"

function Invoke-Captured {
  param(
    [string]$FilePath,
    [string[]]$Arguments,
    [int]$TimeoutSeconds
  )

  $startInfo = New-Object Diagnostics.ProcessStartInfo
  $startInfo.FileName = $FilePath
  $startInfo.Arguments = (($Arguments | ForEach-Object {
    if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
  }) -join " ")
  $startInfo.UseShellExecute = $false
  $startInfo.CreateNoWindow = $true
  $startInfo.RedirectStandardOutput = $true
  $startInfo.RedirectStandardError = $true

  $process = New-Object Diagnostics.Process
  $process.StartInfo = $startInfo
  [void]$process.Start()

  if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    try { $process.Kill() } catch {}
    return [PSCustomObject]@{
      ExitCode = -1
      TimedOut = $true
      Output = "TIMED_OUT after $TimeoutSeconds seconds"
    }
  }

  $output = ($process.StandardOutput.ReadToEnd() + "`n" + $process.StandardError.ReadToEnd()).Trim()
  return [PSCustomObject]@{
    ExitCode = $process.ExitCode
    TimedOut = $false
    Output = $output
  }
}

function Test-TcpPort {
  param(
    [string]$HostName,
    [int]$Port,
    [int]$TimeoutSeconds = 3
  )

  $client = New-Object Net.Sockets.TcpClient
  $async = $null
  try {
    $async = $client.BeginConnect($HostName, $Port, $null, $null)
    if (-not $async.AsyncWaitHandle.WaitOne($TimeoutSeconds * 1000, $false)) {
      return $false
    }
    $client.EndConnect($async)
    return $client.Connected
  } catch {
    return $false
  } finally {
    if ($async -and $async.AsyncWaitHandle) {
      $async.AsyncWaitHandle.Close()
    }
    $client.Close()
  }
}

$tailscale = Get-Command tailscale.exe -ErrorAction SilentlyContinue
if (-not $tailscale) {
  throw "tailscale.exe was not found."
}

$drive = $DriveLetter.Trim().TrimEnd(":").ToUpperInvariant()
$netUse = Invoke-Captured -FilePath "$env:SystemRoot\System32\net.exe" -Arguments @("use", "$drive`:") -TimeoutSeconds $CommandTimeoutSeconds
$port445 = Test-TcpPort -HostName $Peer -Port 445
$netcheck = Invoke-Captured -FilePath $tailscale.Source -Arguments @("netcheck") -TimeoutSeconds $CommandTimeoutSeconds
$status = Invoke-Captured -FilePath $tailscale.Source -Arguments @("status") -TimeoutSeconds $CommandTimeoutSeconds
$ping = Invoke-Captured -FilePath $tailscale.Source -Arguments @(
  "ping",
  "--c=$PingCount",
  "--timeout=$PingTimeoutSeconds` s".Replace(" ", ""),
  $Peer
) -TimeoutSeconds ($PingCount * $PingTimeoutSeconds + 5)

$mappingMatches = $netUse.ExitCode -eq 0 -and
  $netUse.Output.IndexOf($ExpectedShare, [StringComparison]::OrdinalIgnoreCase) -ge 0
$direct = $ping.Output -match "(?im)^pong from .+ via (?!DERP\()[^\r\n]+:[0-9]+"
$relay = $ping.Output -match "(?i)DERP\(|relay"

Write-Host "=== tailscale netcheck ==="
Write-Host $netcheck.Output
Write-Host "=== tailscale status ==="
Write-Host $status.Output
Write-Host "=== tailscale ping ==="
Write-Host $ping.Output
Write-Host "=== SMB ==="
Write-Host "mapping_matches=$mappingMatches"
Write-Host "tcp_445=$port445"
Write-Host "direct=$direct"
Write-Host "relay_seen=$relay"

if (-not $mappingMatches) {
  throw "$drive`: is not mapped to $ExpectedShare."
}
if (-not $port445) {
  throw "TCP 445 is not reachable on $Peer."
}
if ($RequireDirect -and -not $direct) {
  throw "A direct Tailscale path was not established."
}

if ($direct) {
  Write-Host "TAILSCALE_DIRECT_OK"
} else {
  Write-Host "TAILSCALE_RELAY_ONLY"
}
Write-Host "SMB_PATH_OK"
