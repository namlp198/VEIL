@echo off
rem Removes the installed VEIL (found through the registry, so it is safe to run from the build package).
rem Add -RemoveData to delete the settings too, or -DryRun to preview.
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0uninstall.ps1" %*
set RC=%ERRORLEVEL%
pause
exit /b %RC%
