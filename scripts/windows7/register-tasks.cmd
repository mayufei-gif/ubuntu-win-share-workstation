@echo off
setlocal EnableExtensions DisableDelayedExpansion

if /I "%~1"=="--self-test" (
  where schtasks.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  where wscript.exe >nul 2>&1
  if errorlevel 1 exit /b 1
  echo REGISTER_TASKS_SELF_TEST_OK
  exit /b 0
)

call "%~dp0load-config.cmd"
set "TASK_LOGON=Ubuntu Win Share Win7 Logon"
set "TASK_HEAL=Ubuntu Win Share Win7 Self Heal"

net session >nul 2>&1
if errorlevel 1 (
  echo Administrator rights are required to register the Win7 recovery tasks.
  echo Open Command Prompt as Administrator and run this script again.
  exit /b 5
)

for %%I in ("%~dp0run-hidden.vbs") do set "RUNNER=%%~sI"
set "TASK_COMMAND=wscript.exe //B //Nologo %RUNNER%"

schtasks /Create /F /TN "%TASK_LOGON%" /SC ONLOGON /TR "%TASK_COMMAND%" /RL LIMITED
if errorlevel 1 exit /b 1

schtasks /Create /F /TN "%TASK_HEAL%" /SC MINUTE /MO %SELF_HEAL_MINUTES% /TR "%TASK_COMMAND%" /RL LIMITED
if errorlevel 1 exit /b 1

echo REGISTER_TASKS_OK interval=%SELF_HEAL_MINUTES%
exit /b 0
