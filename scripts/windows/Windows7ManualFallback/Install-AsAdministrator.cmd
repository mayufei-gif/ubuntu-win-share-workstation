@echo off
setlocal
cd /d "%~dp0"

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Repair-Windows7Dependencies.ps1" -LaunchClient
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
  echo.
  echo Repair failed. Read the message above, then retry after fixing the reported dependency.
  pause
)

exit /b %EXIT_CODE%
