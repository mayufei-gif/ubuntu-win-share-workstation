@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  echo ACCEPTANCE_TEST_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"

call "%~dp0win7-readiness.cmd"
if errorlevel 1 exit /b 10

call "%~dp0ensure-share.cmd"
if errorlevel 1 (
  echo SHARE_RECOVERY_FAILED
  exit /b 11
)

call "%~dp0test-share-sync.cmd"
if errorlevel 1 exit /b 12

schtasks /Query /TN "Ubuntu Win Share Win7 Logon" >nul 2>&1
if errorlevel 1 (
  echo LOGON_TASK_MISSING
  exit /b 13
)

schtasks /Query /TN "Ubuntu Win Share Win7 Self Heal" >nul 2>&1
if errorlevel 1 (
  echo SELF_HEAL_TASK_MISSING
  exit /b 14
)

if not defined GITHUB_REMOTE (
  echo GITHUB_REMOTE_NOT_CONFIGURED
  exit /b 15
)

git ls-remote "%GITHUB_REMOTE%" HEAD >nul 2>&1
if errorlevel 1 (
  echo GITHUB_REMOTE_QUERY_FAILED
  exit /b 16
)

echo WIN7_ACCEPTANCE_OK
exit /b 0
