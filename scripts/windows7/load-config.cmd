@echo off
if exist "%~dp0share-config.local.cmd" call "%~dp0share-config.local.cmd"
if not defined SHARE_DRIVE set "SHARE_DRIVE=I:"
if not defined SELF_HEAL_MINUTES set "SELF_HEAL_MINUTES=10"
if not defined GITHUB_REMOTE set "GITHUB_REMOTE=git@github.com:mayufei-gif/ubuntu-win-share-workstation.git"
exit /b 0
