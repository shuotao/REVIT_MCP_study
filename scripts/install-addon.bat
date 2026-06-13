@echo off
setlocal

set "SCRIPT_DIR=%~dp0"
set "PS1=%SCRIPT_DIR%install-addon.ps1"

if not exist "%PS1%" (
    echo [ERROR] Missing PowerShell installer: "%PS1%"
    pause
    exit /b 1
)

echo [INFO] Launching PowerShell installer...
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%"
set "EXIT_CODE=%ERRORLEVEL%"

if not "%EXIT_CODE%"=="0" (
    echo.
    echo [ERROR] install-addon.ps1 failed with exit code %EXIT_CODE%.
    pause
    exit /b %EXIT_CODE%
)

exit /b 0
