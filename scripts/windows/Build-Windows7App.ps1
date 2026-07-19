[CmdletBinding()]
param(
  [string]$Version = "0.3.1",
  [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sourceRoot = Join-Path $repositoryRoot "src\windows7\UbuntuWinShareClient"
$outputRoot = Join-Path $repositoryRoot $OutputDirectory
$outputPath = Join-Path $outputRoot "UbuntuWinShare-Win7-$Version.exe"
$selfTestResult = Join-Path $outputRoot "UbuntuWinShare-Win7-$Version.self-test.txt"
$compilerCandidates = @(
  (Join-Path $env:WINDIR "Microsoft.NET\Framework\v3.5\csc.exe"),
  (Join-Path $env:WINDIR "Microsoft.NET\Framework64\v3.5\csc.exe")
)
$compiler = $compilerCandidates |
  Where-Object { Test-Path -LiteralPath $_ } |
  Select-Object -First 1
$assemblyInfoPath = Join-Path $sourceRoot "AssemblyInfo.cs"

if (-not $compiler) {
  throw ".NET Framework 3.5 C# compiler was not found."
}

$assemblyInfo = Get-Content -LiteralPath $assemblyInfoPath -Raw
$expectedFileVersion = [Regex]::Escape($Version + ".0")
if ($assemblyInfo -notmatch "AssemblyFileVersion\(`"$expectedFileVersion`"\)") {
  throw "AssemblyInfo.cs does not match requested release version $Version."
}

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if (Test-Path -LiteralPath $outputPath) {
  Remove-Item -Force -LiteralPath $outputPath
}
if (Test-Path -LiteralPath $selfTestResult) {
  Remove-Item -Force -LiteralPath $selfTestResult
}

$sources = Get-ChildItem -LiteralPath $sourceRoot -Filter *.cs |
  Sort-Object Name |
  ForEach-Object { $_.FullName }

$arguments = @(
  "/nologo",
  "/target:winexe",
  "/platform:anycpu",
  "/optimize+",
  "/codepage:65001",
  "/out:$outputPath",
  "/win32manifest:$(Join-Path $sourceRoot 'app.manifest')",
  "/reference:System.dll",
  "/reference:System.Core.dll",
  "/reference:System.Drawing.dll",
  "/reference:System.Security.dll",
  "/reference:System.Windows.Forms.dll",
  "/reference:System.Xml.dll"
) + $sources

& $compiler $arguments
if ($LASTEXITCODE -ne 0) {
  throw "Windows 7 client compilation failed with exit code $LASTEXITCODE."
}

$process = Start-Process `
  -FilePath $outputPath `
  -ArgumentList @("--self-test", "--result", $selfTestResult) `
  -PassThru `
  -Wait
if ($process.ExitCode -ne 0) {
  throw "Windows 7 client self-test failed with exit code $($process.ExitCode)."
}

$selfTestText = Get-Content -LiteralPath $selfTestResult -Raw
if ($selfTestText -notmatch "WIN7_CLIENT_SELF_TEST_OK") {
  throw "Windows 7 client self-test marker is missing."
}

$hash = (Get-FileHash -LiteralPath $outputPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$outputPath.sha256"
[IO.File]::WriteAllText(
  $hashPath,
  "$hash  $([IO.Path]::GetFileName($outputPath))`n",
  [Text.UTF8Encoding]::new($false))

Write-Host "WIN7_APP_BUILD_OK path=$outputPath sha256=$hash"
