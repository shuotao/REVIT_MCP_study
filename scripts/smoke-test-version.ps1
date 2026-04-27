<#
.SYNOPSIS
  RevitMCP 多版本 Smoke Test 輔助腳本

.DESCRIPTION
  逐一切換 Revit 版本後執行 smoke-test.js。
  每次只能有一個 Revit 版本監聽 port 8964，請依提示開啟對應版本的 Revit。

.PARAMETER Versions
  要測試的 Revit 版本清單，預設 2020,2021,2022,2023,2024,2025,2026

.PARAMETER SkipPrompt
  跳過「請開啟 Revit」提示，直接執行（適合已知 Revit 已在跑的情境）

.EXAMPLE
  # 測試單一版本（Revit 2023 已開啟）
  .\scripts\smoke-test-version.ps1 -Versions 2023 -SkipPrompt

  # 依序測試多版本（每次會暫停等你換 Revit）
  .\scripts\smoke-test-version.ps1 -Versions 2022,2023,2024
#>

param(
    [string[]]$Versions = @('2020','2021','2022','2023','2024','2025','2026'),
    [switch]$SkipPrompt
)

$mcpServerDir = Join-Path $PSScriptRoot "..\MCP-Server"
$results = @()

foreach ($ver in $Versions) {
    if (-not $SkipPrompt) {
        Write-Host ""
        Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
        Write-Host "  準備測試 Revit $ver" -ForegroundColor Cyan
        Write-Host "  請確認：" -ForegroundColor Yellow
        Write-Host "    1. 關閉其他 Revit 版本" -ForegroundColor Yellow
        Write-Host "    2. 開啟 Revit $ver" -ForegroundColor Yellow
        Write-Host "    3. 在 Revit 中按下「啟動 MCP 服務」" -ForegroundColor Yellow
        Write-Host "════════════════════════════════════════" -ForegroundColor Cyan
        Read-Host "按 Enter 繼續，或 Ctrl+C 中止"
    }

    Write-Host ""
    Write-Host ">>> 執行 smoke-test --revit $ver" -ForegroundColor Green

    $output = & node "$mcpServerDir\scripts\smoke-test.js" --revit $ver 2>&1
    $exitCode = $LASTEXITCODE

    $output | ForEach-Object { Write-Host $_ }

    $passed = if ($exitCode -eq 0) { "✅ PASS" } else { "❌ FAIL" }
    $results += [PSCustomObject]@{
        Version = "Revit $ver"
        Result  = $passed
        Exit    = $exitCode
    }
}

Write-Host ""
Write-Host "════════ 多版本測試摘要 ════════" -ForegroundColor Cyan
$results | Format-Table -AutoSize

$totalFail = ($results | Where-Object { $_.Exit -ne 0 }).Count
if ($totalFail -eq 0) {
    Write-Host "🟢 全部通過" -ForegroundColor Green
} else {
    Write-Host "🔴 $totalFail 個版本失敗" -ForegroundColor Red
    exit 1
}
