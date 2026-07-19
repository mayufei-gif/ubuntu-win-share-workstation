@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  echo COLLECT_DIAGNOSTICS_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"

set "OUTPUT=%~1"
if not defined OUTPUT set "OUTPUT=%USERPROFILE%\Desktop\ubuntu-win-share-win7-diagnostics.txt"

> "%OUTPUT%" echo Ubuntu Win Share Windows 7 Diagnostics
>>"%OUTPUT%" echo Generated: %DATE% %TIME%
>>"%OUTPUT%" echo.
>>"%OUTPUT%" echo [OS]
>>"%OUTPUT%" ver
wmic os get Caption,Version,ServicePackMajorVersion,OSArchitecture /value >>"%OUTPUT%" 2>&1
>>"%OUTPUT%" echo.
>>"%OUTPUT%" echo [CONFIG]
>>"%OUTPUT%" echo SHARE_DRIVE=%SHARE_DRIVE%
>>"%OUTPUT%" echo SHARE_UNC=%SHARE_UNC%
>>"%OUTPUT%" echo SSH_HOST=%SSH_HOST%
>>"%OUTPUT%" echo SSH_REMOTE_PATH=%SSH_REMOTE_PATH%
>>"%OUTPUT%" echo SELF_HEAL_MINUTES=%SELF_HEAL_MINUTES%
>>"%OUTPUT%" echo GITHUB_REMOTE=%GITHUB_REMOTE%
>>"%OUTPUT%" echo.
>>"%OUTPUT%" echo [NETWORK]
ipconfig /all >>"%OUTPUT%" 2>&1
route print >>"%OUTPUT%" 2>&1
>>"%OUTPUT%" echo.
>>"%OUTPUT%" echo [SMB]
net use >>"%OUTPUT%" 2>&1
sc query lanmanworkstation >>"%OUTPUT%" 2>&1
>>"%OUTPUT%" echo.
>>"%OUTPUT%" echo [TASKS]
schtasks /Query /TN "Ubuntu Win Share Win7 Logon" /V /FO LIST >>"%OUTPUT%" 2>&1
schtasks /Query /TN "Ubuntu Win Share Win7 Self Heal" /V /FO LIST >>"%OUTPUT%" 2>&1
>>"%OUTPUT%" echo.
>>"%OUTPUT%" echo [GIT]
git --version >>"%OUTPUT%" 2>&1
ssh -V >>"%OUTPUT%" 2>&1
git remote -v >>"%OUTPUT%" 2>&1

echo DIAGNOSTICS_OK path=%OUTPUT%
exit /b 0
