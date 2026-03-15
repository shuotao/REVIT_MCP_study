# Revit MCP 快速部署腳本 (2024 版本)
$ErrorActionPreference = "Stop"

$sourceDllDir = "h:\0_REVIT MCP\REVIT_MCP_study-main\MCP\bin\Release.2024"
$sourceAddin = "h:\0_REVIT MCP\REVIT_MCP_study-main\MCP\RevitMCP.2024.addin"
$targetDir = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\RevitMCP"
$targetAddin = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2024\RevitMCP.addin"

Write-Host "--- 開始部署 Revit MCP ---" -ForegroundColor Cyan

# 1. 建立目錄
if (-not (Test-Path $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Write-Host "建立目錄: $targetDir" -ForegroundColor Green
}

# 2. 複製 DLL 與相依項
if (Test-Path "$sourceDllDir\*.dll") {
    Copy-Item "$sourceDllDir\*.dll" -Destination $targetDir -Force
    Write-Host "✅ 已複製 DLL 檔案" -ForegroundColor Green
}
else {
    Write-Error "找不到來源 DLL (請確認 dotnet build 已成功)"
}

# 3. 複製並命名 Addin
if (Test-Path $sourceAddin) {
    Copy-Item $sourceAddin -Destination $targetAddin -Force
    Write-Host "✅ 已複製 Addin 檔案" -ForegroundColor Green
}

Write-Host "--- 部署完成！請重新開啟 Revit 並點選法蘭 ---" -ForegroundColor Cyan
