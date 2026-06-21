# verify-pr42-pr43.ps1
# Windows-only verification script for ChimingLu PRs #42 / #43 (core-reload framework).
# Run from repo root in PowerShell.
# 對應 plan: docs/handoff-pr-chiminlu.md 章節 12.3 Win-2

$ErrorActionPreference = 'Stop'
$RepoRoot = (Resolve-Path "$PSScriptRoot/../..").Path
Set-Location $RepoRoot

Write-Host "==> Step 1: Switch to verify/pr42 (chiminlu/feat/core-reload-logic)" -ForegroundColor Cyan
git checkout verify/pr42
if ($LASTEXITCODE -ne 0) { throw "git checkout verify/pr42 failed. Did Mac session push branches?" }

Write-Host "`n==> Step 2: Restore + build for Revit 2024 (Release.R24, .NET FX 4.8)" -ForegroundColor Cyan
Push-Location MCP
dotnet restore RevitMCP.csproj
dotnet build -c Release.R24 RevitMCP.csproj
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "Build Release.R24 FAILED — do not merge. Comment failure on PR #42." }
Pop-Location

$DllPath = "$RepoRoot\MCP\bin\Release.R24\RevitMCP.dll"
if (-not (Test-Path $DllPath)) { throw "Expected DLL not found at $DllPath" }
Write-Host "  Build OK: $DllPath" -ForegroundColor Green

Write-Host "`n==> Step 3: Deploy to Revit 2024 add-ins folder" -ForegroundColor Cyan
$DeployRoot = "$env:APPDATA\Autodesk\Revit\Addins\2024\RevitMCP"
New-Item -ItemType Directory -Force -Path $DeployRoot | Out-Null
$BuildDir = "$RepoRoot\MCP\bin\Release.R24"
Copy-Item -Path "$BuildDir\*.dll" -Destination $DeployRoot -Force
if (Test-Path "$BuildDir\runtime") {
  Copy-Item -Path "$BuildDir\runtime" -Destination $DeployRoot -Recurse -Force
}
Copy-Item -Path "$RepoRoot\MCP\RevitMCP.addin" -Destination "$env:APPDATA\Autodesk\Revit\Addins\2024\" -Force
Write-Host "  Deployed to: $DeployRoot" -ForegroundColor Green

Write-Host "`n==> Step 4: MANUAL CHECK — open Revit 2024 and verify" -ForegroundColor Yellow
Write-Host @"
  1. Launch Revit 2024
  2. Open any project (or create new)
  3. Go to ribbon: 增益集 → MCP Tools panel
  4. Confirm 'Core 重載' button appears
  5. Click 'Core 重載'
  6. Open the RevitMCP Real-time Log Viewer
  7. Expect log lines like:
     [INFO] CoreRuntime 開始熱重載...
     [INFO] CoreRuntime 卸載中...
     [INFO] Shadow-copy 路徑: C:\Users\...\Local\Temp\...
     [INFO] CoreRuntime 已載入
     [INFO] CoreRuntime 熱重載完成

  If ALL above pass: continue to Step 5
  If ANY fail: STOP. Comment failure on PR #42 with screenshot.
"@ -ForegroundColor Yellow

$ans = Read-Host "`nDid Revit 2024 verification PASS? (y/N)"
if ($ans -ne 'y' -and $ans -ne 'Y') {
  Write-Host "Aborted. Do not run merge step. Investigate and report to PR #42." -ForegroundColor Red
  exit 1
}

Write-Host "`n==> Step 5: Merge verify/pr43 on top to test infra/scripts together" -ForegroundColor Cyan
git merge --no-ff verify/pr43
if ($LASTEXITCODE -ne 0) {
  Write-Host "Merge conflict between #42 and #43. Resolve before merging both PRs." -ForegroundColor Red
  exit 2
}

Write-Host "`n==> Step 6: Run setup.ps1 dry-check + smoke test" -ForegroundColor Cyan
& "$RepoRoot\scripts\preflight-check.ps1"
& "$RepoRoot\scripts\verify-installation.ps1"
if (Test-Path "$RepoRoot\scripts\smoke-test-version.ps1") {
  & "$RepoRoot\scripts\smoke-test-version.ps1"
}

Write-Host "`n==> Verification COMPLETE" -ForegroundColor Green
Write-Host "Next: follow docs/handoff-pr-chiminlu.md section 12.3 Win-3 to admin merge." -ForegroundColor Cyan
Write-Host "Reset local branch back to main when done: git checkout main" -ForegroundColor Cyan
