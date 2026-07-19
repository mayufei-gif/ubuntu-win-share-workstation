@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  echo INSTALL_CLIENT_SELF_TEST_OK
  exit /b 0
)

if "%~1"=="" goto usage

set "SHARE_UNC=%~1"
set "SHARE_DRIVE=%~2"
set "SHARE_USER=%~3"
set "SSH_HOST=%~4"
set "SSH_REMOTE_PATH=%~5"
set "SELF_HEAL_MINUTES=%~6"

if not defined SHARE_DRIVE set "SHARE_DRIVE=I:"
if not defined SELF_HEAL_MINUTES set "SELF_HEAL_MINUTES=10"

> "%~dp0share-config.local.cmd" (
  echo @echo off
  echo set "SHARE_DRIVE=%SHARE_DRIVE%"
  echo set "SHARE_UNC=%SHARE_UNC%"
  echo set "SHARE_USER=%SHARE_USER%"
  echo set "SSH_HOST=%SSH_HOST%"
  echo set "SSH_REMOTE_PATH=%SSH_REMOTE_PATH%"
  echo set "SELF_HEAL_MINUTES=%SELF_HEAL_MINUTES%"
  echo set "GITHUB_REMOTE=git@github.com:mayufei-gif/ubuntu-win-share-workstation.git"
)

call "%~dp0map-share.cmd"
if errorlevel 1 exit /b 1

call "%~dp0register-tasks.cmd"
if errorlevel 1 exit /b 1

echo WIN7_CLIENT_INSTALL_OK
exit /b 0

:usage
echo Usage:
echo   install-client.cmd ^<UNC^> ^<DRIVE^> ^<SMB_USER^> ^<SSH_HOST^> ^<REMOTE_PATH^> [SELF_HEAL_MINUTES]
echo Example:
echo   install-client.cmd \\192.168.1.50\ubuntu-win I: mana mana@192.168.1.50 /home/mana/C/ubuntu-win 10
exit /b 2
