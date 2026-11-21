@echo off
start "" /wait "%~dp0ImageChecker.exe" --install
if errorlevel 1 exit /b 1
echo ImageChecker is ready. Right-click images or folders and choose ImageChecker.
echo On Windows 11, choose Show more options to see the menu.
pause
