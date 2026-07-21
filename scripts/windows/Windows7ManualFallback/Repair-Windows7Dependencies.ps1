param(
  [switch]$LaunchClient,
  [switch]$SelfTest
)

$ErrorActionPreference = "Stop"
$scriptPath = $MyInvocation.MyCommand.Path
$scriptRoot = Split-Path -Parent $scriptPath

function Test-Administrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = New-Object Security.Principal.WindowsPrincipal($identity)
  return $principal.IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Test-NetFx35 {
  try {
    $netFx = Get-ItemProperty `
      -Path "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5" `
      -ErrorAction Stop
    return [int]$netFx.Install -eq 1
  }
  catch {
    return $false
  }
}

function Test-Robocopy {
  return Test-Path -LiteralPath (Join-Path $env:WINDIR "System32\robocopy.exe")
}

function Ensure-Dependencies {
  if (-not (Test-NetFx35)) {
    $dism = Join-Path $env:WINDIR "System32\dism.exe"
    if (-not (Test-Path -LiteralPath $dism)) {
      throw "DISM is missing. Windows 7 SP1 is required."
    }
    & $dism /online /enable-feature /featurename:NetFx3 /all /norestart
    if ($LASTEXITCODE -ne 0) {
      throw "DISM failed while enabling .NET Framework 3.5.1: $LASTEXITCODE"
    }
  }

  $workstation = Get-Service -Name "LanmanWorkstation" -ErrorAction Stop
  if ($workstation.Status -ne "Running") {
    Start-Service -Name "LanmanWorkstation" -ErrorAction Stop
  }

  if (-not (Test-NetFx35)) {
    throw ".NET Framework 3.5.1 is still disabled after DISM completed."
  }
  if (-not (Test-Robocopy)) {
    throw "robocopy.exe is missing. Windows 7 SP1 is required."
  }
}

if ($SelfTest) {
  if (-not (Test-Path -LiteralPath $scriptPath)) {
    throw "Manual fallback script path is unavailable."
  }
  Write-Output "WIN7_MANUAL_FALLBACK_SELF_TEST_OK"
  exit 0
}

if (-not (Test-Administrator)) {
  $arguments = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File " +
    ('"' + $scriptPath + '"')
  if ($LaunchClient) {
    $arguments += " -LaunchClient"
  }
  $elevated = Start-Process `
    -FilePath (Join-Path $env:WINDIR "System32\WindowsPowerShell\v1.0\powershell.exe") `
    -Verb RunAs `
    -ArgumentList $arguments `
    -PassThru `
    -Wait
  exit $elevated.ExitCode
}

Ensure-Dependencies

if ($LaunchClient) {
  $clientPath = Join-Path $scriptRoot "UbuntuWinShare-Win7-0.3.2-Client.exe"
  if (-not (Test-Path -LiteralPath $clientPath)) {
    throw "Client payload is missing: $clientPath"
  }
  Start-Process -FilePath $clientPath
}

Write-Output "WIN7_DEPENDENCY_REPAIR_OK"
