@echo off
setlocal EnableExtensions

if /I "%~1"=="--self-test" (
  echo UNREGISTER_TASKS_SELF_TEST_OK
  exit /b 0
)

call :delete_task "Ubuntu Win Share Win7 Logon"
if errorlevel 1 exit /b 5
call :delete_task "Ubuntu Win Share Win7 Self Heal"
if errorlevel 1 exit /b 5
echo UNREGISTER_TASKS_OK
exit /b 0

:delete_task
schtasks /Query /TN "%~1" >nul 2>&1
if errorlevel 1 exit /b 0
schtasks /Delete /F /TN "%~1" >nul 2>&1
exit /b %ERRORLEVEL%
