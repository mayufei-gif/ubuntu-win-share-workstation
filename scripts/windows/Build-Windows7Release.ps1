[CmdletBinding()]
param(
  [string]$Version = "0.3.1",
  [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$outputRoot = Join-Path $repositoryRoot $OutputDirectory
$appBuilder = Join-Path $PSScriptRoot "Build-Windows7App.ps1"
$exeName = "UbuntuWinShare-Win7-$Version.exe"
$exePath = Join-Path $outputRoot $exeName
$exeHashPath = "$exePath.sha256"
$guidePath = Join-Path $repositoryRoot "docs\Windows7_客户安装说明.txt"
$licensePath = Join-Path $repositoryRoot "LICENSE"
$stagingRoot = Join-Path $env:TEMP (
  "ubuntu-win-share-customer-" + [Guid]::NewGuid().ToString("N"))
$packageName = "UbuntuWinShare-Win7-$Version-customer"
$packageRoot = Join-Path $stagingRoot $packageName
$zipPath = Join-Path $outputRoot ($packageName + ".zip")
$zipHashPath = "$zipPath.sha256"

& $appBuilder -Version $Version -OutputDirectory $OutputDirectory

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item -LiteralPath $exePath -Destination $packageRoot
Copy-Item -LiteralPath $exeHashPath -Destination $packageRoot
Copy-Item -LiteralPath $guidePath -Destination $packageRoot
Copy-Item -LiteralPath $licensePath -Destination $packageRoot

if (Test-Path -LiteralPath $zipPath) {
  Remove-Item -Force -LiteralPath $zipPath
}

try {
  Compress-Archive `
    -Path (Join-Path $stagingRoot $packageName) `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal
}
finally {
  if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -Recurse -Force -LiteralPath $stagingRoot
  }
}

$zipHash = (
  Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
  $zipHashPath,
  "$zipHash  $([IO.Path]::GetFileName($zipPath))`n",
  [Text.UTF8Encoding]::new($false))

Write-Host "WIN7_RELEASE_BUILD_OK path=$zipPath sha256=$zipHash"
