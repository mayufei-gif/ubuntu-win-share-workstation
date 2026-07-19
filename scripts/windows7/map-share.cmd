@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  where net.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  echo MAP_SHARE_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"
if not defined SHARE_UNC (
  echo SHARE_UNC is not configured.
  exit /b 2
)

dir "%SHARE_DRIVE%\" >nul 2>&1
if not errorlevel 1 (
  echo %SHARE_DRIVE% is already available.
  exit /b 0
)

net use "%SHARE_DRIVE%" /delete /y >nul 2>&1

if defined SHARE_USER (
  net use "%SHARE_DRIVE%" "%SHARE_UNC%" /user:"%SHARE_USER%" * /persistent:yes
) else (
  net use "%SHARE_DRIVE%" "%SHARE_UNC%" /persistent:yes
)

if errorlevel 1 exit /b 1
dir "%SHARE_DRIVE%\" >nul 2>&1
if errorlevel 1 exit /b 1

echo MAP_SHARE_OK drive=%SHARE_DRIVE% share=%SHARE_UNC%
exit /b 0
