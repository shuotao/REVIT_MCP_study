@echo off
echo 1. Receive source from the central repository
echo 2. Share reviewed commits from my-revit-tool
echo 3. Prepare an upstream merge for review
choice /c 123 /n /m "Choose 1, 2 or 3: "
set "abbyAction=Pull"
if errorlevel 3 (set "abbyAction=Upstream") else if errorlevel 2 (set "abbyAction=Push")
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\abby\Sync-AbbySource.ps1" -Action %abbyAction%
set "abbyExit=%errorlevel%"
pause
exit /b %abbyExit%
