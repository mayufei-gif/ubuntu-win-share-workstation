@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  echo ENSURE_SHARE_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"
if not defined SHARE_UNC exit /b 2
dir "%SHARE_DRIVE%\" >nul 2>&1
if not errorlevel 1 exit /b 0

net use "%SHARE_DRIVE%" "%SHARE_UNC%" /persistent:yes >nul 2>&1
dir "%SHARE_DRIVE%\" >nul 2>&1
if not errorlevel 1 exit /b 0

set "LOG_DIR=%LOCALAPPDATA%\UbuntuWinShare"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%" >nul 2>&1
set "LOG_FILE=%LOG_DIR%\ensure.log"
if exist "%LOG_FILE%" for %%A in ("%LOG_FILE%") do if %%~zA GTR 1048576 del /q "%LOG_FILE%" >nul 2>&1
echo %DATE% %TIME% remount failed drive=%SHARE_DRIVE% share=%SHARE_UNC%>>"%LOG_FILE%"
exit /b 1
