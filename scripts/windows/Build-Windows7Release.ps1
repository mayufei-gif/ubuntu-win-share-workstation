[CmdletBinding()]
param(
  [string]$Version = "0.3.2",
  [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$outputRoot = Join-Path $repositoryRoot $OutputDirectory
$bootstrapperBuilder = Join-Path $PSScriptRoot "Build-Windows7Bootstrapper.ps1"
$setupName = "UbuntuWinShare-Win7-$Version-Setup.exe"
$setupPath = Join-Path $outputRoot $setupName
$setupHashPath = "$setupPath.sha256"
$clientName = "UbuntuWinShare-Win7-$Version.exe"
$clientPath = Join-Path $outputRoot $clientName
$clientHashPath = "$clientPath.sha256"
$uninstallerName = "UbuntuWinShare-Win7-$Version-Uninstall.exe"
$uninstallerPath = Join-Path $outputRoot $uninstallerName
$uninstallerHashPath = "$uninstallerPath.sha256"
$guidePath = Join-Path $repositoryRoot "docs\Windows7_客户安装说明.txt"
$acceptancePath = Join-Path $repositoryRoot "docs\Windows7_实机验收清单.txt"
$licensePath = Join-Path $repositoryRoot "LICENSE"
$stagingRoot = Join-Path $env:TEMP (
  "ubuntu-win-share-customer-" + [Guid]::NewGuid().ToString("N"))
$packageName = "UbuntuWinShare-Win7-$Version-customer"
$packageRoot = Join-Path $stagingRoot $packageName
$zipPath = Join-Path $outputRoot ($packageName + ".zip")
$zipHashPath = "$zipPath.sha256"
$packageManifestPath = Join-Path $packageRoot "SHA256SUMS.txt"
$releaseManifestPath = Join-Path $outputRoot "SHA256SUMS.txt"
$manualFallbackSource = Join-Path $PSScriptRoot "Windows7ManualFallback"
$manualFallbackRoot = Join-Path $packageRoot "人工兜底"
$manualClientName = "UbuntuWinShare-Win7-$Version-Client.exe"
$manualClientPath = Join-Path $manualFallbackRoot $manualClientName
$manualClientHashPath = "$manualClientPath.sha256"

function Write-Sha256Manifest {
  param(
    [string[]]$Paths,
    [string]$ManifestPath
  )

  $lines = @(
    $Paths |
      Sort-Object { [IO.Path]::GetFileName($_) } |
      ForEach-Object {
        $hash = (
          Get-FileHash -LiteralPath $_ -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        "$hash  $([IO.Path]::GetFileName($_))"
      }
  )
  [IO.File]::WriteAllText(
    $ManifestPath,
    ($lines -join "`n") + "`n",
    [Text.UTF8Encoding]::new($false))
}

& $bootstrapperBuilder -Version $Version -OutputDirectory $OutputDirectory

New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
New-Item -ItemType Directory -Force -Path $manualFallbackRoot | Out-Null
Copy-Item -LiteralPath $setupPath -Destination $packageRoot
Copy-Item -LiteralPath $setupHashPath -Destination $packageRoot
Copy-Item -LiteralPath $uninstallerPath -Destination $packageRoot
Copy-Item -LiteralPath $uninstallerHashPath -Destination $packageRoot
Copy-Item -LiteralPath $guidePath -Destination $packageRoot
Copy-Item -LiteralPath $acceptancePath -Destination $packageRoot
Copy-Item -LiteralPath $licensePath -Destination $packageRoot
Copy-Item `
  -LiteralPath (Join-Path $manualFallbackSource "Install-AsAdministrator.cmd") `
  -Destination (Join-Path $manualFallbackRoot "安装_管理员.cmd")
Copy-Item `
  -LiteralPath (Join-Path $manualFallbackSource "Repair-Windows7Dependencies.ps1") `
  -Destination $manualFallbackRoot
Copy-Item `
  -LiteralPath $clientPath `
  -Destination $manualClientPath
$manualClientHash = (Get-FileHash -LiteralPath $manualClientPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(
  $manualClientHashPath,
  "$manualClientHash  $manualClientName`n",
  [Text.UTF8Encoding]::new($false))
Write-Sha256Manifest `
  -Paths @(
    (Join-Path $packageRoot $setupName),
    (Join-Path $packageRoot ([IO.Path]::GetFileName($setupHashPath))),
    (Join-Path $packageRoot $uninstallerName),
    (Join-Path $packageRoot ([IO.Path]::GetFileName($uninstallerHashPath))),
    (Join-Path $packageRoot ([IO.Path]::GetFileName($guidePath))),
    (Join-Path $packageRoot ([IO.Path]::GetFileName($acceptancePath))),
    (Join-Path $packageRoot ([IO.Path]::GetFileName($licensePath))),
    (Join-Path $manualFallbackRoot "安装_管理员.cmd"),
    (Join-Path $manualFallbackRoot "Repair-Windows7Dependencies.ps1"),
    $manualClientPath,
    $manualClientHashPath
  ) `
  -ManifestPath $packageManifestPath

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
Write-Sha256Manifest `
  -Paths @(
    $setupPath,
    $uninstallerPath,
    $zipPath
  ) `
  -ManifestPath $releaseManifestPath

Write-Host "WIN7_RELEASE_BUILD_OK path=$zipPath sha256=$zipHash"
Write-Host "WIN7_RELEASE_MANIFEST_OK path=$releaseManifestPath"
