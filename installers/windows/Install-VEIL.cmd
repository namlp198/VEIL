@echo off
rem Machine-wide install (asks for administrator rights). Pass extra options through, e.g. -NoLaunch.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1" -Scope Machine %*
set RC=%ERRORLEVEL%
if not "%RC%"=="0" echo Setup finished with exit code %RC%.
pause
exit /b %RC%
