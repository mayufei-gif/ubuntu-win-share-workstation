[CmdletBinding()]
param(
  [string]$Version = "0.3.2",
  [string]$OutputDirectory = "artifacts"
)

$ErrorActionPreference = "Stop"

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$sourceRoot = Join-Path $repositoryRoot "src\windows7\UbuntuWinShareBootstrapper"
$outputRoot = Join-Path $repositoryRoot $OutputDirectory
$setupName = "UbuntuWinShare-Win7-$Version-Setup.exe"
$setupPath = Join-Path $outputRoot $setupName
$setupHashPath = "$setupPath.sha256"
$appBuilder = Join-Path $PSScriptRoot "Build-Windows7App.ps1"
$msvcRoot = "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\MSVC\14.16.27023"
$visualStudioIde = "C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE"
$sdkRoot = "C:\Program Files (x86)\Windows Kits\10"
$sdkVersion = "10.0.19041.0"
$temporaryRoot = Join-Path $env:TEMP (
  "UbuntuWinShareBootstrapper-" + [Guid]::NewGuid().ToString("N"))

if ($Version -ne "0.3.2") {
  throw "The embedded bootstrapper resource currently supports release version 0.3.2 only."
}

$requiredPaths = @(
  (Join-Path $msvcRoot "bin\Hostx64\x86\cl.exe"),
  (Join-Path $msvcRoot "bin\Hostx64\x86\link.exe"),
  (Join-Path $msvcRoot "bin\Hostx64\x64\mspdbcore.dll"),
  (Join-Path $sdkRoot "bin\$sdkVersion\x86\rc.exe"),
  (Join-Path $sdkRoot "bin\$sdkVersion\x86\mt.exe")
)
$missingPath = $requiredPaths |
  Where-Object { -not (Test-Path -LiteralPath $_) } |
  Select-Object -First 1
if ($missingPath) {
  throw "Windows x86 build tool is missing: $missingPath"
}

& $appBuilder -Version $Version -OutputDirectory $OutputDirectory

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

$temporaryObject = Join-Path $temporaryRoot "Bootstrapper.obj"
$temporaryResource = Join-Path $temporaryRoot "bootstrapper.res"
$temporarySetup = Join-Path $temporaryRoot $setupName
$selfTestPath = Join-Path $temporaryRoot "bootstrapper-self-test.txt"
$manifestPath = Join-Path $temporaryRoot "bootstrapper.manifest.xml"

$originalPath = $env:PATH
$originalInclude = $env:INCLUDE
$originalLib = $env:LIB
$env:PATH = (
  (Join-Path $msvcRoot "bin\Hostx64\x86"),
  (Join-Path $msvcRoot "bin\Hostx64\x64"),
  $visualStudioIde,
  (Join-Path $sdkRoot "bin\$sdkVersion\x86"),
  $originalPath
) -join ";"
$env:INCLUDE = (
  (Join-Path $msvcRoot "include"),
  (Join-Path $sdkRoot "Include\$sdkVersion\ucrt"),
  (Join-Path $sdkRoot "Include\$sdkVersion\shared"),
  (Join-Path $sdkRoot "Include\$sdkVersion\um"),
  (Join-Path $sdkRoot "Include\$sdkVersion\winrt")
) -join ";"
$env:LIB = (
  (Join-Path $msvcRoot "lib\x86"),
  (Join-Path $sdkRoot "Lib\$sdkVersion\ucrt\x86"),
  (Join-Path $sdkRoot "Lib\$sdkVersion\um\x86")
) -join ";"

try {
  Push-Location $sourceRoot
  try {
    & rc.exe /nologo ("/fo" + $temporaryResource) "bootstrapper.rc"
    if ($LASTEXITCODE -ne 0) {
      throw "Bootstrapper resource compilation failed with exit code $LASTEXITCODE."
    }

    & cl.exe /nologo /std:c++14 /EHsc /MT /Z7 /W4 /DUNICODE /D_UNICODE `
      /c "Bootstrapper.cpp" ("/Fo" + $temporaryObject)
    if ($LASTEXITCODE -ne 0) {
      throw "Bootstrapper x86 compilation failed with exit code $LASTEXITCODE."
    }
  }
  finally {
    Pop-Location
  }

  & link.exe /nologo /subsystem:windows /machine:X86 ("/out:" + $temporarySetup) `
    $temporaryObject $temporaryResource Advapi32.lib Shell32.lib User32.lib
  if ($LASTEXITCODE -ne 0) {
    throw "Bootstrapper x86 link failed with exit code $LASTEXITCODE."
  }

  & mt.exe ("-inputresource:" + $temporarySetup + ";#1") ("-out:" + $manifestPath)
  if ($LASTEXITCODE -ne 0) {
    throw "Bootstrapper manifest extraction failed with exit code $LASTEXITCODE."
  }
  $manifest = Get-Content -LiteralPath $manifestPath -Raw
  if ($manifest -notmatch 'requestedExecutionLevel level="asInvoker"') {
    throw "Bootstrapper asInvoker manifest marker is missing."
  }

  $process = Start-Process `
    -FilePath $temporarySetup `
    -ArgumentList @("--self-test", "--result", $selfTestPath) `
    -WindowStyle Hidden `
    -PassThru `
    -Wait
  if ($process.ExitCode -ne 0) {
    throw "Bootstrapper self-test failed with exit code $($process.ExitCode)."
  }
  $selfTest = Get-Content -LiteralPath $selfTestPath -Raw
  if ($selfTest -notmatch "WIN7_BOOTSTRAPPER_SELF_TEST_OK" -or
      $selfTest -notmatch "CLIENT_PAYLOAD=present") {
    throw "Bootstrapper self-test markers are missing."
  }

  Copy-Item -LiteralPath $temporarySetup -Destination $setupPath -Force
  $hash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
  [IO.File]::WriteAllText(
    $setupHashPath,
    "$hash  $setupName`n",
    [Text.UTF8Encoding]::new($false))
  Write-Host "WIN7_BOOTSTRAPPER_BUILD_OK path=$setupPath sha256=$hash"
}
finally {
  $env:PATH = $originalPath
  $env:INCLUDE = $originalInclude
  $env:LIB = $originalLib
  if (Test-Path -LiteralPath $temporaryRoot) {
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
  }
}
