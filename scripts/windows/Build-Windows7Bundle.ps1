[CmdletBinding()]
param(
  [string]$Version = "dev",
  [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$outputRoot = Join-Path $repositoryRoot $OutputDirectory
$stagingRoot = Join-Path $env:TEMP ("ubuntu-win-share-win7-" + [Guid]::NewGuid().ToString("N"))
$bundleName = "ubuntu-win-share-workstation-win7-$Version"
$bundleRoot = Join-Path $stagingRoot $bundleName
$zipPath = Join-Path $outputRoot ($bundleName + ".zip")
$hashPath = $zipPath + ".sha256"

New-Item -ItemType Directory -Force -Path $bundleRoot | Out-Null
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

Copy-Item -Recurse -Force -Path (Join-Path $repositoryRoot "scripts\windows7") -Destination (Join-Path $bundleRoot "scripts")
Copy-Item -Force -Path (Join-Path $repositoryRoot "docs\windows7-compatibility.md") -Destination $bundleRoot
Copy-Item -Force -Path (Join-Path $repositoryRoot "docs\windows7-acceptance-checklist.md") -Destination $bundleRoot
Copy-Item -Force -Path (Join-Path $repositoryRoot "docs\sync-verification.md") -Destination $bundleRoot
Copy-Item -Force -Path (Join-Path $repositoryRoot "README.md") -Destination $bundleRoot
Copy-Item -Force -Path (Join-Path $repositoryRoot "LICENSE") -Destination $bundleRoot

if (Test-Path -LiteralPath $zipPath) {
  Remove-Item -Force -LiteralPath $zipPath
}

Compress-Archive -Path (Join-Path $stagingRoot $bundleName) -DestinationPath $zipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($hashPath, "$hash  $([IO.Path]::GetFileName($zipPath))`n", [Text.UTF8Encoding]::new($false))

Remove-Item -Recurse -Force -LiteralPath $stagingRoot

Write-Host "WIN7_BUNDLE_OK path=$zipPath sha256=$hash"
