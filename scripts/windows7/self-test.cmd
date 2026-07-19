@echo off
setlocal EnableExtensions

call "%~dp0install-client.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0map-share.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0ensure-share.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0register-tasks.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0unregister-tasks.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0test-share-sync.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0git-workflow.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0win7-readiness.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0acceptance-test.cmd" --self-test
if errorlevel 1 exit /b 1
call "%~dp0collect-diagnostics.cmd" --self-test
if errorlevel 1 exit /b 1

echo WINDOWS7_CMD_SELF_TEST_OK
exit /b 0
