# Deprecated: Use verify-qaqc.ps1 instead
# This script has been replaced by the comprehensive QA/QC verification script.
#
# Usage:
#   .\scripts\verify-qaqc.ps1              # Full QA/QC (all phases)
#   .\scripts\verify-qaqc.ps1 -SkipBuild   # Skip build phase (structure check only)
#   .\scripts\verify-qaqc.ps1 -Version 2024  # Build only for specific version

Write-Host ""
Write-Host "This script has been replaced by verify-qaqc.ps1" -ForegroundColor Yellow
Write-Host ""
Write-Host "Running verify-qaqc.ps1 -SkipBuild instead..." -ForegroundColor Cyan
Write-Host ""

& "$PSScriptRoot\verify-qaqc.ps1" -SkipBuild -SkipDeploy
