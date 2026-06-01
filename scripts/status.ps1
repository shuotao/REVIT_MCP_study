# Revit MCP local status report
# Usage:
#   .\scripts\status.ps1
#
# Note:
# Platform account usage windows such as 5-hour, 7-day, and total usage are
# not exposed to this local repository script. They remain "unavailable" unless
# the AI client provides those metrics through its own UI or API.

param(
    [switch]$Json
)

$ErrorActionPreference = "Continue"

$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent -Path $scriptDir

function Read-Text {
    param([string]$Path)
    try {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            return [System.IO.File]::ReadAllText($Path)
        }
    } catch {}
    return $null
}

function Get-RuntimeToolCount {
    Push-Location $projectRoot
    try {
        $script = "import('./MCP-Server/build/tools/index.js').then(m=>console.log(m.registerRevitTools().length)).catch(()=>process.exit(2))"
        $result = & node --input-type=module -e $script 2>$null
        if ($LASTEXITCODE -eq 0 -and $result -match '^\d+$') {
            return [int]$result
        }
    } catch {
    } finally {
        Pop-Location
    }

    $hits = Select-String -Path "$projectRoot\MCP-Server\src\tools\*.ts" `
        -Pattern '^\s+name:\s*[''"]' -ErrorAction SilentlyContinue
    return $hits.Count
}

function Get-GitBranch {
    Push-Location $projectRoot
    try {
        $branch = & git rev-parse --abbrev-ref HEAD 2>$null
        if ($LASTEXITCODE -eq 0) { return ($branch | Select-Object -First 1) }
    } catch {
    } finally {
        Pop-Location
    }
    return "unavailable"
}

function Get-GitDirtyCount {
    Push-Location $projectRoot
    try {
        $lines = & git status --short 2>$null
        if ($LASTEXITCODE -eq 0) { return @($lines).Count }
    } catch {
    } finally {
        Pop-Location
    }
    return $null
}

function Test-Port {
    param([int]$Port)
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $iar = $client.BeginConnect("127.0.0.1", $Port, $null, $null)
        $ok = $iar.AsyncWaitHandle.WaitOne(800, $false)
        if ($ok -and $client.Connected) {
            $client.Close()
            return $true
        }
        $client.Close()
    } catch {}
    return $false
}

$domainRootCount = (Get-ChildItem -Path "$projectRoot\domain" -Filter "*.md" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "README.md" }).Count
$domainRefCount = (Get-ChildItem -Path "$projectRoot\domain\references" -Filter "*.md" -ErrorAction SilentlyContinue).Count
$skillCount = (Get-ChildItem -Path "$projectRoot\.claude\skills\*\SKILL.md" -ErrorAction SilentlyContinue).Count
$toolCount = Get-RuntimeToolCount

$configText = Read-Text "$projectRoot\MCP\Configuration\config.json"
$port = 8964
if ($configText) {
    try {
        $config = $configText | ConvertFrom-Json
        if ($config.port) { $port = [int]$config.port }
    } catch {}
}

$status = [ordered]@{
    timestamp = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    project = $projectRoot
    model = "Codex based on GPT-5 (per current assistant system context)"
    usage = [ordered]@{
        five_hour = "unavailable"
        seven_day = "unavailable"
        total = "unavailable"
        note = "Account-level usage metrics are not exposed to this repo or script."
    }
    git = [ordered]@{
        branch = Get-GitBranch
        dirty_files = Get-GitDirtyCount
    }
    mcp = [ordered]@{
        port = $port
        port_open = Test-Port -Port $port
        config = "MCP/Configuration/config.json"
        server_entry = "MCP-Server/build/index.js"
    }
    counts = [ordered]@{
        runtime_tools = $toolCount
        domain_sop_files = ($domainRootCount + $domainRefCount)
        skills = $skillCount
    }
    checks = [ordered]@{
        qaqc_docs = ".\scripts\verify-qaqc.ps1 -SkipBuild -SkipDeploy"
        qaqc_full = ".\scripts\verify-qaqc.ps1 -Version 2024"
    }
}

if ($Json) {
    $status | ConvertTo-Json -Depth 8
    exit 0
}

Write-Host ""
Write-Host "Revit MCP Status" -ForegroundColor Cyan
Write-Host "----------------"
Write-Host ("Time          : {0}" -f $status.timestamp)
Write-Host ("Project       : {0}" -f $status.project)
Write-Host ("Model         : {0}" -f $status.model)
Write-Host ""
Write-Host "Usage"
Write-Host ("  5-hour      : {0}" -f $status.usage.five_hour)
Write-Host ("  7-day       : {0}" -f $status.usage.seven_day)
Write-Host ("  Total       : {0}" -f $status.usage.total)
Write-Host ("  Note        : {0}" -f $status.usage.note)
Write-Host ""
Write-Host "Repository"
Write-Host ("  Branch      : {0}" -f $status.git.branch)
Write-Host ("  Dirty files : {0}" -f $status.git.dirty_files)
Write-Host ""
Write-Host "MCP"
Write-Host ("  Port        : {0}" -f $status.mcp.port)
Write-Host ("  Port open   : {0}" -f $status.mcp.port_open)
Write-Host ("  Server      : {0}" -f $status.mcp.server_entry)
Write-Host ""
Write-Host "Counts"
Write-Host ("  Tools       : {0}" -f $status.counts.runtime_tools)
Write-Host ("  Domain      : {0}" -f $status.counts.domain_sop_files)
Write-Host ("  Skills      : {0}" -f $status.counts.skills)
Write-Host ""
Write-Host "Checks"
Write-Host ("  Docs QAQC   : {0}" -f $status.checks.qaqc_docs)
Write-Host ("  Full QAQC   : {0}" -f $status.checks.qaqc_full)
Write-Host ""
