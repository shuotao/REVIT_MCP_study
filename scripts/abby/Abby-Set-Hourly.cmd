@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Set-AbbyUpdateSchedule.ps1"
set "abbyExit=%errorlevel%"
pause
exit /b %abbyExit%
