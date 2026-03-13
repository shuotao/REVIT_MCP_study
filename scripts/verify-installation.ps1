# Revit MCP Installation Verification Script
# Simple version without complex hashtable

$ErrorActionPreference = "Continue"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Revit MCP Installation Verification" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""

$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent -Path $scriptDir

Write-Host "Project Root: $projectRoot" -ForegroundColor Yellow
Write-Host ""

# Check 1: Directory Structure
Write-Host "[Check 1] Directory Structure..." -ForegroundColor Cyan
if (Test-Path "$projectRoot\MCP\MCP") {
    Write-Host "  WARNING: Old MCP\MCP\ structure detected" -ForegroundColor Yellow
}
else {
    Write-Host "  PASS: Correct MCP\ single-layer structure" -ForegroundColor Green
}

# Check 2: Project Files (Unified Build Only)
Write-Host ""
Write-Host "[Check 2] Project Files..." -ForegroundColor Cyan
if (Test-Path "$projectRoot\MCP\RevitMCP.csproj") {
    Write-Host "  FOUND: RevitMCP.csproj (Unified Build - Nice3point SDK)" -ForegroundColor Green
}
else {
    Write-Host "  ERROR: No .csproj file found" -ForegroundColor Red
}
# Warn about legacy files that should NOT exist
if (Test-Path "$projectRoot\MCP\RevitMCP.2024.csproj") {
    Write-Host "  ERROR: RevitMCP.2024.csproj exists (LEGACY - should be deleted!)" -ForegroundColor Red
    Write-Host "    This file causes build confusion. Delete it." -ForegroundColor Red
}
if (Test-Path "$projectRoot\MCP\RevitMCP.2024.addin") {
    Write-Host "  ERROR: RevitMCP.2024.addin exists (DUPLICATE - should be deleted!)" -ForegroundColor Red
    Write-Host "    Duplicate .addin files cause Revit to load the add-in twice." -ForegroundColor Red
}
if (Test-Path "$projectRoot\MCP\Core\RevitCompatibility.cs") {
    Write-Host "  FOUND: RevitCompatibility.cs (Cross-version layer)" -ForegroundColor Green
}
else {
    Write-Host "  WARNING: RevitCompatibility.cs missing" -ForegroundColor Yellow
}

# Check 2b: Duplicate .addin files in Revit Addins folders
Write-Host ""
Write-Host "[Check 2b] Duplicate .addin Check (All Installed Versions)..." -ForegroundColor Cyan
$appDataPath = $env:APPDATA
$supportedVersions = @("2022", "2023", "2024", "2025", "2026")
$duplicateFound = $false
foreach ($ver in $supportedVersions) {
    $addinsDir = Join-Path $appDataPath "Autodesk\Revit\Addins\$ver"
    if (Test-Path $addinsDir) {
        $addinFiles = Get-ChildItem -Path $addinsDir -Filter "*.addin" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "RevitMCP|revit-mcp" }
        if ($addinFiles.Count -gt 1) {
            Write-Host "  ERROR: Revit $ver has $($addinFiles.Count) .addin files:" -ForegroundColor Red
            foreach ($f in $addinFiles) {
                Write-Host "    - $($f.FullName)" -ForegroundColor Red
            }
            Write-Host "    Keep only ONE .addin file to prevent duplicate loading." -ForegroundColor Red
            $duplicateFound = $true
        }
        elseif ($addinFiles.Count -eq 1) {
            Write-Host "  PASS: Revit $ver has 1 .addin file" -ForegroundColor Green
        }
    }
}
if (-not $duplicateFound) {
    Write-Host "  PASS: No duplicate .addin files detected" -ForegroundColor Green
}

# Check 3: Built DLL
Write-Host ""
Write-Host "[Check 3] Built DLL..." -ForegroundColor Cyan
$foundDll = $false
if (Test-Path "$projectRoot\MCP\bin\Release\RevitMCP.dll") {
    $dll = Get-Item "$projectRoot\MCP\bin\Release\RevitMCP.dll"
    Write-Host "  FOUND: RevitMCP.dll (Unified Release)" -ForegroundColor Green
    Write-Host "    Size: $($dll.Length) bytes" -ForegroundColor Gray
    Write-Host "    Modified: $($dll.LastWriteTime)" -ForegroundColor Gray
    $foundDll = $true
}
elseif (Test-Path "$projectRoot\MCP\bin\Release.2024\RevitMCP.dll") {
    Write-Host "  FOUND: RevitMCP.dll (Legacy 2024 Release)" -ForegroundColor Yellow
    $foundDll = $true
}
else {
    Write-Host "  WARNING: DLL not built yet" -ForegroundColor Yellow
    Write-Host "    Need to run: dotnet build -c Release.R24 RevitMCP.csproj" -ForegroundColor Gray
}

# Check 4: Install Script
Write-Host ""
Write-Host "[Check 4] Install Script..." -ForegroundColor Cyan
if (Test-Path "$scriptDir\install-addon-bom.ps1") {
    $scriptContent = Get-Content "$scriptDir\install-addon-bom.ps1" -Raw
    if ($scriptContent -match 'MCP\\\\MCP\\\\') {
        Write-Host "  ERROR: Script still contains MCP\\MCP\\ paths" -ForegroundColor Red
    }
    else {
        Write-Host "  PASS: Script paths corrected" -ForegroundColor Green
    }
}

# Summary
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host ""

if (-not $foundDll) {
    Write-Host "1. Build the project (choose your Revit version):" -ForegroundColor Yellow
    Write-Host "   cd `"$projectRoot\MCP`"" -ForegroundColor Gray
    Write-Host "   dotnet build -c Release.R22 RevitMCP.csproj   # Revit 2022" -ForegroundColor Gray
    Write-Host "   dotnet build -c Release.R24 RevitMCP.csproj   # Revit 2024" -ForegroundColor Gray
    Write-Host "   dotnet build -c Release.R25 RevitMCP.csproj   # Revit 2025" -ForegroundColor Gray
    Write-Host "   dotnet build -c Release.R26 RevitMCP.csproj   # Revit 2026" -ForegroundColor Gray
    Write-Host ""
}

Write-Host "2. Run installation:" -ForegroundColor Yellow
Write-Host "   .\scripts\install-addon-bom.ps1" -ForegroundColor Gray
Write-Host ""

Write-Host "3. Verify in Revit:" -ForegroundColor Yellow
Write-Host "   - Close all Revit instances" -ForegroundColor Gray
Write-Host "   - Restart Revit" -ForegroundColor Gray
Write-Host "   - Check for MCP Tools panel" -ForegroundColor Gray
Write-Host ""

Read-Host "Press Enter to continue"
