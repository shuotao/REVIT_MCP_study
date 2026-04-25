#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Legacy wrapper: keep this entrypoint for backward compatibility.
# All deployment logic is centralized in install-addon.ps1.
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($scriptDir)) {
    $scriptDir = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
}

$mainInstaller = Join-Path $scriptDir "install-addon.ps1"
if (-not (Test-Path $mainInstaller)) {
    Write-Host "ERROR: cannot find main installer script: $mainInstaller" -ForegroundColor Red
    exit 1
}

Write-Host "[Info] install-addon-bom.ps1 已轉為相容入口，將執行 install-addon.ps1" -ForegroundColor Yellow
& $mainInstaller
exit $LASTEXITCODE
