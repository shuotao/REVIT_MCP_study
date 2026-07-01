# backfill-domain-metadata.ps1 — wrapper for the Python backfill script
# 需要 Python 3 已安裝（執行 `python` 指令可呼叫到）

$ErrorActionPreference = "Stop"

$repoRoot = git rev-parse --show-toplevel
Set-Location $repoRoot

# Check python availability
$pythonCmd = $null
foreach ($candidate in @("python3", "python")) {
    if (Get-Command $candidate -ErrorAction SilentlyContinue) {
        $pythonCmd = $candidate
        break
    }
}

if (-not $pythonCmd) {
    Write-Error "需要 Python 3，請先安裝（https://www.python.org/downloads/）"
    exit 1
}

& $pythonCmd "scripts/backfill-domain-metadata.py" @args
