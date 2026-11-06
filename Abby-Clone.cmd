@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\abby\Clone-Abby.ps1" %*
set "abbyExit=%errorlevel%"
pause
exit /b %abbyExit%
