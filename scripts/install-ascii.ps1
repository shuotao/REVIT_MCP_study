# ============================================================================
# Revit MCP Add-in Auto Installer (Safe ASCII Version)
# ============================================================================

$ErrorActionPreference = "Stop"
$appDataPath = $env:APPDATA
$projectRoot = "d:\David\BIM MCP\REVIT_MCP_study"
$revitVersion = "2024" # Default for this task

Write-Host "Revit MCP Add-in Installer" -ForegroundColor Cyan

# 1. Check Paths
$addonPath = Join-Path $appDataPath "Autodesk\Revit\Addins\$revitVersion\RevitMCP"
$sourceDll = Join-Path $projectRoot "MCP\bin\Release.2024\RevitMCP.dll"
$sourceAddin = Join-Path $projectRoot "MCP\RevitMCP.2024.addin"

Write-Host "Target Path: $addonPath"
Write-Host "Source DLL: $sourceDll"

if (-not (Test-Path $sourceDll)) {
    Write-Host "ERROR: Source DLL not found at $sourceDll" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $sourceAddin)) {
    Write-Host "ERROR: Source Addin not found at $sourceAddin" -ForegroundColor Red
    exit 1
}

# 2. Create Directory
if (-not (Test-Path $addonPath)) {
    Write-Host "Creating directory $addonPath"
    New-Item -ItemType Directory -Path $addonPath -Force | Out-Null
}

# 3. Copy Files
Write-Host "Copying files..."
Copy-Item -Path $sourceDll -Destination (Join-Path $addonPath "RevitMCP.dll") -Force
Copy-Item -Path $sourceAddin -Destination (Join-Path $addonPath "RevitMCP.addin") -Force

Write-Host "DONE! Installation successful." -ForegroundColor Green
