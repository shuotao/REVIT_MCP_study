@echo off
setlocal enabledelayedexpansion

:: =====================================================================
:: Revit Addin 安裝腳本
:: 用途: 將 RevitMCP.addin 與 RevitMCP.dll 複製到各版本 Revit Addins 資料夾
:: =====================================================================

:: 設定來源路徑 (以此腳本所在目錄為基準)
set "SCRIPT_DIR=%~dp0"
set "ROOT_DIR=%SCRIPT_DIR%.."
set "ADDIN_SOURCE=%ROOT_DIR%\MCP\RevitMCP.addin"
set "DLL_SOURCE=%ROOT_DIR%\MCP\bin\Debug\RevitMCP.dll"

echo [資訊] 開始安裝 Revit MCP 外掛...
echo [資訊] 來源 Addin: %ADDIN_SOURCE%
echo [資訊] 來源 DLL: %DLL_SOURCE%
echo.

:: 檢查來源檔案
if not exist "%ADDIN_SOURCE%" (
    echo [錯誤] 找不到 Addin 檔案: "%ADDIN_SOURCE%"
    goto :ERROR
)

if not exist "%DLL_SOURCE%" (
    echo [警告] 找不到 Debug DLL 檔案: "%DLL_SOURCE%"
    echo [提示] 請確保已在 Visual Studio 中以 Debug 模式完成編譯。
    goto :ERROR
)

:: 定義目標 Revit 版本
set "VERSIONS=2020 2021 2022 2023 2024 2025 2026"

:: 執行複製動作
for %%V in (%VERSIONS%) do (
    set "TARGET_FOLDER=%AppData%\Autodesk\Revit\Addins\%%V"
    
    echo [處理] Revit %%V...
    
    :: 建立資料夾 (如果不存在)
    if not exist "!TARGET_FOLDER!" (
        echo     建立資料夾: !TARGET_FOLDER!
        mkdir "!TARGET_FOLDER!"
    )
    
    :: 複製檔案 (/Y 表示不提示直接覆蓋)
    copy /Y "%ADDIN_SOURCE%" "!TARGET_FOLDER!\" >nul
    if !errorlevel! equ 0 (
        echo     成功複製 RevitMCP.addin
    ) else (
        echo     [錯誤] 無法複製 RevitMCP.addin
    )
    
    copy /Y "%DLL_SOURCE%" "!TARGET_FOLDER!\" >nul
    if !errorlevel! equ 0 (
        echo     成功複製 RevitMCP.dll
    ) else (
        echo     [錯誤] 無法複製 RevitMCP.dll
    )
    echo.
)

echo [完成] 安裝程序執行結束。
pause
exit /b 0

:ERROR
echo.
echo [失敗] 安裝終止，請檢查上述錯誤。
pause
exit /b 1
