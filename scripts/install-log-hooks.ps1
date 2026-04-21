# install-log-hooks.ps1 — 一次性設定 git 使用 repo 內的 hooks 目錄
# 適用於 Windows PowerShell 5.1+ / PowerShell 7+

$ErrorActionPreference = "Stop"

$repoRoot = git rev-parse --show-toplevel
Set-Location $repoRoot

# Point git to repo-shared hooks directory
git config core.hooksPath scripts/git-hooks

$current = git config core.hooksPath
$logMonth = Get-Date -Format 'yyyy-MM'

Write-Host "[OK] Git hooks 已設定：$current" -ForegroundColor Green
Write-Host "     下一次 commit 會自動 append 到 log/$logMonth.md"
Write-Host ""
Write-Host "卸載方法：git config --unset core.hooksPath"
