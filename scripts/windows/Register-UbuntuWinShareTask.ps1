[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$Share,

  [string]$DriveLetter = "I",

  [string]$TaskName = "Ubuntu Win Share Logon Mount"
)

$ErrorActionPreference = "Stop"

$scriptPath = Join-Path $PSScriptRoot "Map-UbuntuWinShare.ps1"
if (-not (Test-Path -LiteralPath $scriptPath)) {
  throw "Cannot find $scriptPath"
}

$arguments = @(
  "-NoProfile",
  "-ExecutionPolicy", "Bypass",
  "-WindowStyle", "Hidden",
  "-File", "`"$scriptPath`"",
  "-Share", "`"$Share`"",
  "-DriveLetter", "`"$DriveLetter`"",
  "-Force"
) -join " "

$action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument $arguments
$trigger = New-ScheduledTaskTrigger -AtLogOn
$settings = New-ScheduledTaskSettingsSet `
  -Hidden `
  -AllowStartIfOnBatteries `
  -DontStopIfGoingOnBatteries `
  -MultipleInstances IgnoreNew `
  -ExecutionTimeLimit (New-TimeSpan -Minutes 2)

Register-ScheduledTask `
  -TaskName $TaskName `
  -Action $action `
  -Trigger $trigger `
  -Settings $settings `
  -Description "Hidden logon mount for an Ubuntu SMB share." `
  -Force | Out-Null

Write-Host "Registered hidden logon task: $TaskName"
