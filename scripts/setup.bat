@echo off
REM ============================================================================
REM Revit MCP 一鍵安裝啟動器
REM 雙擊此檔案即可開始安裝，不需要管理員權限
REM ============================================================================
chcp 65001 >nul 2>&1
cd /d "%~dp0"
powershell -ExecutionPolicy Bypass -NoProfile -File "%~dp0setup.ps1" %*
echo.
echo 按任意鍵關閉此視窗...
pause >nul
