@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  where git.exe >nul 2>&1
  if errorlevel 1 (
    echo Git for Windows is not installed.
    exit /b 1
  )
  echo GIT_WORKFLOW_SELF_TEST_OK
  exit /b 0
)

set "ACTION=%~1"
if not defined ACTION goto usage

if /I "%ACTION%"=="status" git status -sb
if /I "%ACTION%"=="pull" git fetch --prune
if /I "%ACTION%"=="pull" if not errorlevel 1 git pull --ff-only
if /I "%ACTION%"=="branch" git checkout -b "%~2"
if /I "%ACTION%"=="publish" git push -u origin HEAD
if /I "%ACTION%"=="gui" start "" git gui
if /I "%ACTION%"=="clone" git clone "%~2" "%~3"

if /I "%ACTION%"=="status" exit /b %ERRORLEVEL%
if /I "%ACTION%"=="pull" exit /b %ERRORLEVEL%
if /I "%ACTION%"=="branch" exit /b %ERRORLEVEL%
if /I "%ACTION%"=="publish" exit /b %ERRORLEVEL%
if /I "%ACTION%"=="gui" exit /b 0
if /I "%ACTION%"=="clone" exit /b %ERRORLEVEL%

:usage
echo Usage:
echo   git-workflow.cmd status
echo   git-workflow.cmd pull
echo   git-workflow.cmd branch ^<name^>
echo   git-workflow.cmd publish
echo   git-workflow.cmd gui
echo   git-workflow.cmd clone ^<repository^> ^<directory^>
exit /b 2
