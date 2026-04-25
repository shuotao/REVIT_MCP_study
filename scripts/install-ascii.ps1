# ============================================================================
# Revit MCP Add-in Auto Installer (Safe ASCII Version)
# ============================================================================

param(
    [ValidateSet("2020", "2021", "2022", "2023", "2024", "2025", "2026")]
    [string]$RevitVersion = "2024"
)

$ErrorActionPreference = "Stop"
$appDataPath = $env:APPDATA
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
}

$projectRoot = (Resolve-Path (Join-Path $scriptDir "..")).Path

$versionConfigMap = @{
    "2020" = "Release.R20"
    "2021" = "Release.R21"
    "2022" = "Release.R22"
    "2023" = "Release.R23"
    "2024" = "Release.R24"
    "2025" = "Release.R25"
    "2026" = "Release.R26"
}

$buildConfig = $versionConfigMap[$RevitVersion]

Write-Host "Revit MCP Add-in Installer" -ForegroundColor Cyan
Write-Host "Version: $RevitVersion ($buildConfig)"

# 1. Check paths
$addonBase = Join-Path $appDataPath "Autodesk\Revit\Addins\$RevitVersion"
$addonPath = Join-Path $addonBase "RevitMCP"
$sourceDll = Join-Path $projectRoot "MCP\bin\$buildConfig\RevitMCP.dll"
$sourceAddin = Join-Path $projectRoot "MCP\RevitMCP.addin"

Write-Host "Target Path: $addonPath"
Write-Host "Source DLL: $sourceDll"
Write-Host "Source Addin: $sourceAddin"

if (-not (Test-Path $sourceDll)) {
    Write-Host "ERROR: Source DLL not found at $sourceDll" -ForegroundColor Red
    Write-Host "Run: dotnet build -c $buildConfig RevitMCP.csproj" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $sourceAddin)) {
    Write-Host "ERROR: Source Addin not found at $sourceAddin" -ForegroundColor Red
    exit 1
}

# 2. Create target directories
if (-not (Test-Path $addonBase)) {
    Write-Host "Creating directory $addonBase"
    New-Item -ItemType Directory -Path $addonBase -Force | Out-Null
}

if (-not (Test-Path $addonPath)) {
    Write-Host "Creating directory $addonPath"
    New-Item -ItemType Directory -Path $addonPath -Force | Out-Null
}

# 3. Copy files (.addin in base, DLL in subfolder)
Write-Host "Copying files..."
Copy-Item -Path $sourceDll -Destination (Join-Path $addonPath "RevitMCP.dll") -Force
Copy-Item -Path $sourceAddin -Destination (Join-Path $addonBase "RevitMCP.addin") -Force

Write-Host "DONE! Installation successful." -ForegroundColor Green
