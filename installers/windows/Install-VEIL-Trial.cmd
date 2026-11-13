@echo off
rem Trial install for the current user only. No administrator rights needed.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Scope User %*
set RC=%ERRORLEVEL%
if not "%RC%"=="0" echo Setup finished with exit code %RC%.
pause
exit /b %RC%
