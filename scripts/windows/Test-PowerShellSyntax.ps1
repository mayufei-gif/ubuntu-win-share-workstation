[CmdletBinding()]
param(
  [string]$Path
)

$ErrorActionPreference = "Stop"
$failed = $false

if (-not $Path) {
  $scriptRoot = $PSScriptRoot
  if (-not $scriptRoot) {
    $scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
  }
  $Path = (Resolve-Path (Join-Path $scriptRoot "..\..")).Path
}

Get-ChildItem -Path $Path -Recurse -Filter "*.ps1" | ForEach-Object {
  $tokens = $null
  $errors = $null
  [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors) | Out-Null
  if ($errors.Count -gt 0) {
    $failed = $true
    Write-Error "$($_.FullName): $($errors | Out-String)"
  }
}

if ($failed) { exit 1 }
Write-Host "POWERSHELL_SYNTAX_OK"
