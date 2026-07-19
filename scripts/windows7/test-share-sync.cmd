@echo off
setlocal EnableExtensions EnableDelayedExpansion

if /I "%~1"=="--self-test" (
  where certutil.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  echo TEST_SHARE_SYNC_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"
if not defined SSH_HOST (
  echo SSH_HOST is not configured.
  exit /b 2
)
if not defined SSH_REMOTE_PATH (
  echo SSH_REMOTE_PATH is not configured.
  exit /b 2
)
dir "%SHARE_DRIVE%\" >nul 2>&1
if errorlevel 1 (
  echo %SHARE_DRIVE% is not available.
  exit /b 3
)

set "PROBE_NAME=_ubuntu_win7_share_probe_%COMPUTERNAME%_%RANDOM%_%RANDOM%.txt"
set "PROBE_PATH=%SHARE_DRIVE%\%PROBE_NAME%"

> "%PROBE_PATH%" (
  echo source=windows7
  echo computer=%COMPUTERNAME%
  echo phase=modified
  echo tick=%DATE% %TIME%
)

set "LOCAL_HASH="
for /f "skip=1 tokens=*" %%H in ('certutil -hashfile "%PROBE_PATH%" SHA256') do (
  if not defined LOCAL_HASH set "LOCAL_HASH=%%H"
)
set "LOCAL_HASH=!LOCAL_HASH: =!"

set "SSH_EXE="
where ssh.exe >nul 2>&1
if not errorlevel 1 set "SSH_EXE=ssh.exe"
if not defined SSH_EXE if exist "%ProgramFiles%\Git\usr\bin\ssh.exe" for %%S in ("%ProgramFiles%\Git\usr\bin\ssh.exe") do set "SSH_EXE=%%~sS"
if not defined SSH_EXE if exist "%ProgramFiles(x86)%\Git\usr\bin\ssh.exe" for %%S in ("%ProgramFiles(x86)%\Git\usr\bin\ssh.exe") do set "SSH_EXE=%%~sS"
if not defined SSH_EXE (
  echo ssh.exe was not found. Install Git for Windows.
  exit /b 4
)

set "REMOTE_HASH="
set "SSH_OUTPUT=%TEMP%\ubuntu_win_share_ssh_%RANDOM%_%RANDOM%.txt"
!SSH_EXE! -o BatchMode=yes !SSH_HOST! sha256sum !SSH_REMOTE_PATH!/!PROBE_NAME! >"!SSH_OUTPUT!" 2>nul
if errorlevel 1 (
  del /q "!SSH_OUTPUT!" >nul 2>&1
  del /q "%PROBE_PATH%" >nul 2>&1
  echo SSH_HASH_QUERY_FAILED
  exit /b 5
)
for /f "usebackq tokens=1" %%H in ("!SSH_OUTPUT!") do (
  if not defined REMOTE_HASH set "REMOTE_HASH=%%H"
)
del /q "!SSH_OUTPUT!" >nul 2>&1

echo Windows sha256: !LOCAL_HASH!
echo Ubuntu sha256: !REMOTE_HASH!

if /I not "!LOCAL_HASH!"=="!REMOTE_HASH!" (
  del /q "%PROBE_PATH%" >nul 2>&1
  echo SYNC_HASH_MISMATCH
  exit /b 5
)

del /q "%PROBE_PATH%" >nul 2>&1
echo SYNC_OK
exit /b 0
