[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$Share,

  [string]$DriveLetter = "I",

  [System.Management.Automation.PSCredential]$Credential,

  [switch]$Force
)

$ErrorActionPreference = "Stop"

function Normalize-DriveName {
  param([string]$Value)
  $name = $Value.Trim().TrimEnd(":")
  if ($name.Length -ne 1) {
    throw "DriveLetter must be a single letter, got '$Value'."
  }
  return $name.ToUpperInvariant()
}

function Get-UncHost {
  param([string]$UncPath)
  if ($UncPath -notmatch "^\\\\([^\\]+)\\([^\\]+)") {
    throw "Share must be a UNC path like \\server\\share."
  }
  return $Matches[1]
}

$driveName = Normalize-DriveName $DriveLetter
$uncHost = Get-UncHost $Share

$portCheck = Test-NetConnection -ComputerName $uncHost -Port 445 -InformationLevel Quiet
if (-not $portCheck) {
  throw "SMB port 445 is not reachable on '$uncHost'. Check the network or tailnet first."
}

$existing = Get-PSDrive -Name $driveName -ErrorAction SilentlyContinue
if ($existing) {
  $currentRoot = [string]$existing.DisplayRoot
  if (-not $currentRoot) { $currentRoot = [string]$existing.Root }
  if ($currentRoot -ieq $Share) {
    Write-Host "$driveName`: is already mapped to $Share"
    exit 0
  }
  if (-not $Force) {
    throw "$driveName`: is already mapped to '$currentRoot'. Use -Force to replace it."
  }
  net use "$driveName`:" /delete /y | Out-Null
}

$parameters = @{
  Name       = $driveName
  PSProvider = "FileSystem"
  Root       = $Share
  Persist    = $true
  Scope      = "Global"
}
if ($Credential) {
  $parameters["Credential"] = $Credential
}

New-PSDrive @parameters | Out-Null

$mapped = Get-PSDrive -Name $driveName -ErrorAction Stop
Write-Host "Mapped $driveName`: to $($mapped.DisplayRoot)"
