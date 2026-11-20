@echo off
start "" /wait "%~dp0ImageChecker.exe" --uninstall
if errorlevel 1 exit /b 1
echo Removed the ImageChecker menu for this Windows account.
pause
