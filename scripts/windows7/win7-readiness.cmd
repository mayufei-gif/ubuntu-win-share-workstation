@echo off
setlocal EnableExtensions EnableDelayedExpansion

if /I "%~1"=="--self-test" (
  where cmd.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  where net.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  where certutil.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  echo WIN7_READINESS_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"
set "FAILED=0"

set "OS_VERSION="
set "SP_MAJOR="
for /f "tokens=2 delims==" %%V in ('wmic os get Version /value 2^>nul ^| find "="') do set "OS_VERSION=%%V"
for /f "tokens=2 delims==" %%V in ('wmic os get ServicePackMajorVersion /value 2^>nul ^| find "="') do set "SP_MAJOR=%%V"

echo OS_VERSION=!OS_VERSION!
echo SERVICE_PACK_MAJOR=!SP_MAJOR!

if not "!OS_VERSION:~0,4!"=="6.1." (
  echo WINDOWS7_OS_NOT_DETECTED
  set "FAILED=1"
)
if not defined SP_MAJOR (
  echo SERVICE_PACK_NOT_DETECTED
  set "FAILED=1"
) else if !SP_MAJOR! LSS 1 (
  echo WINDOWS7_SP1_REQUIRED
  set "FAILED=1"
)

for %%C in (net.exe certutil.exe schtasks.exe wscript.exe sc.exe) do (
  where %%C >nul 2>&1
  if errorlevel 1 (
    echo REQUIRED_COMMAND_MISSING=%%C
    set "FAILED=1"
  )
)

sc query lanmanworkstation | find "RUNNING" >nul 2>&1
if errorlevel 1 (
  echo LANMANWORKSTATION_NOT_RUNNING
  set "FAILED=1"
)

where git.exe >nul 2>&1
if errorlevel 1 (
  echo GIT_FOR_WINDOWS_MISSING
  set "FAILED=1"
) else (
  for /f "tokens=3" %%V in ('git --version') do echo GIT_VERSION=%%V
)

where ssh.exe >nul 2>&1
if errorlevel 1 (
  if not exist "%ProgramFiles%\Git\usr\bin\ssh.exe" if not exist "%ProgramFiles(x86)%\Git\usr\bin\ssh.exe" (
    echo SSH_CLIENT_MISSING
    set "FAILED=1"
  )
)

if not defined SHARE_UNC (
  echo SHARE_UNC_NOT_CONFIGURED
  set "FAILED=1"
) else (
  echo SHARE_UNC=!SHARE_UNC!
)

if "!FAILED!"=="1" (
  echo WIN7_READINESS_FAILED
  exit /b 10
)

echo WIN7_READINESS_OK
exit /b 0
