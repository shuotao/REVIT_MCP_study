@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\abby\Update-Abby.ps1" %*
set "abbyExit=%errorlevel%"
echo.
pause
exit /b %abbyExit%
