#requires -Version 5.1
<#
.SYNOPSIS
    Reverse-audit every fork of an upstream GitHub repo via the GitHub API (gh CLI).

.DESCRIPTION
    Enumerates ALL forks of the upstream repo and, for each one, determines:
      1. Used?      - fork exists (everyone forked); did they push anything back?
      2. Changed?   - any branch ahead of upstream (ALL branches checked, not just main)
      3. Issue?     - did this owner open an issue on upstream (PRs filtered out)
      4. PR?        - did this owner open a pull request on upstream

    Output classification per fork:
      PR_SHARED      opened >=1 PR on upstream                (highest value)
      CHANGED_LOCAL  has commits ahead but never opened a PR
      ISSUE_ONLY     no commits ahead but opened an issue
      PURE_FORK      forked, never pushed, no issue/PR
      INACCESSIBLE   fork deleted / private / empty (API error)

    Reports also surface, per fork, the DISTINCT commit author identities
    (name+email) so a shared account (two humans, one login) is visible, and a
    login<->author-name mismatch list so identities never slip past unnoticed.

    NOTE: This is read-only against GitHub. It never adds git remotes.
    The generated report under -OutDir is intended to stay local (gitignored)
    because it names individual collaborators' activity.

.EXAMPLE
    pwsh ./scripts/fork-audit.ps1
    powershell -File scripts/fork-audit.ps1 -Upstream shuotao/REVIT_MCP_study
#>
[CmdletBinding()]
param(
    [string]$Upstream = 'shuotao/REVIT_MCP_study',
    [string]$OutDir   = 'docs/fork-audit',
    [int]$MaxBranchesPerFork = 0   # 0 = all branches
)

$ErrorActionPreference = 'Stop'
$script:ghCalls = 0

# ---------------------------------------------------------------------------
# gh helpers
# ---------------------------------------------------------------------------
function Test-GhReady {
    $null = & gh --version 2>$null
    if ($LASTEXITCODE -ne 0) { throw "gh CLI not found. Install GitHub CLI and run 'gh auth login'." }
    $null = & gh auth status 2>$null
    if ($LASTEXITCODE -ne 0) { throw "gh is not authenticated. Run 'gh auth login'." }
}

function Invoke-RateGuard {
    if ($script:ghCalls -gt 0 -and ($script:ghCalls % 50) -eq 0) {
        $rl = & gh api rate_limit --jq '.resources.core | "\(.remaining) \(.reset)"' 2>$null
        if ($LASTEXITCODE -eq 0 -and $rl) {
            $parts = $rl -split ' '
            $remaining = [int]$parts[0]
            $reset     = [int]$parts[1]
            if ($remaining -lt 100) {
                $waitSec = [Math]::Max(0, $reset - [int][double]::Parse((Get-Date -UFormat %s))) + 5
                Write-Host "  [rate] only $remaining left; sleeping ${waitSec}s until reset..." -ForegroundColor Yellow
                Start-Sleep -Seconds $waitSec
            }
        }
    }
}

# Returns parsed objects (one per JSON-lines row from gh --jq).
function Invoke-GhJson {
    param([Parameter(Mandatory)][string[]]$ApiArgs)
    $script:ghCalls++
    Invoke-RateGuard
    $out = & gh @ApiArgs 2>$null
    if ($LASTEXITCODE -ne 0) { throw "gh api failed: $($ApiArgs -join ' ')" }
    if (-not $out) { return @() }
    return @($out | Where-Object { $_ -and $_.Trim() } | ForEach-Object { $_ | ConvertFrom-Json })
}

# Returns raw string lines (for --jq that emits bare strings, e.g. branch names).
function Invoke-GhRawLines {
    param([Parameter(Mandatory)][string[]]$ApiArgs)
    $script:ghCalls++
    Invoke-RateGuard
    $out = & gh @ApiArgs 2>$null
    if ($LASTEXITCODE -ne 0) { throw "gh api failed: $($ApiArgs -join ' ')" }
    if (-not $out) { return @() }
    # Trim each line: gh on Windows can leave a trailing CR that corrupts URLs.
    return @($out | ForEach-Object { "$_".Trim() } | Where-Object { $_ })
}

# ---------------------------------------------------------------------------
# 0. preflight
# ---------------------------------------------------------------------------
Test-GhReady

$upInfo = Invoke-GhJson @('api', "repos/$Upstream", '--jq',
    '{default: .default_branch, forks: .forks_count, full: .full_name}')
$BaseBranch = $upInfo[0].default
$ForkCount  = $upInfo[0].forks
Write-Host "Upstream $Upstream | default=$BaseBranch | forks=$ForkCount" -ForegroundColor Cyan

# ---------------------------------------------------------------------------
# 1. one-shot: all PRs and all (real) issues on upstream, grouped by login
# ---------------------------------------------------------------------------
Write-Host "Fetching upstream PRs..." -ForegroundColor Cyan
$prs = Invoke-GhJson @('api', "repos/$Upstream/pulls?state=all&per_page=100", '--paginate', '--jq',
    '.[] | {num:.number, login:.user.login, state:.state, merged:(.merged_at != null), title:.title, created:.created_at}')

Write-Host "Fetching upstream issues (filtering out PRs)..." -ForegroundColor Cyan
$issues = Invoke-GhJson @('api', "repos/$Upstream/issues?state=all&per_page=100", '--paginate', '--jq',
    '.[] | select(.pull_request == null) | {num:.number, login:.user.login, state:.state, title:.title, created:.created_at}')

$prsByUser = @{}
foreach ($p in $prs) {
    $k = "$($p.login)".ToLower()
    if (-not $prsByUser.ContainsKey($k)) { $prsByUser[$k] = New-Object System.Collections.ArrayList }
    [void]$prsByUser[$k].Add($p)
}
$issuesByUser = @{}
foreach ($i in $issues) {
    $k = "$($i.login)".ToLower()
    if (-not $issuesByUser.ContainsKey($k)) { $issuesByUser[$k] = New-Object System.Collections.ArrayList }
    [void]$issuesByUser[$k].Add($i)
}
Write-Host "  PRs=$($prs.Count) (authors=$($prsByUser.Keys.Count)) | real issues=$($issues.Count) (authors=$($issuesByUser.Keys.Count))" -ForegroundColor Gray

# ---------------------------------------------------------------------------
# 2. enumerate every fork
# ---------------------------------------------------------------------------
Write-Host "Enumerating forks..." -ForegroundColor Cyan
$forks = Invoke-GhJson @('api', "repos/$Upstream/forks?per_page=100", '--paginate', '--jq',
    '.[] | {login:.owner.login, full:.full_name, created:.created_at, pushed:.pushed_at, default:.default_branch, url:.html_url}')
Write-Host "  got $($forks.Count) forks" -ForegroundColor Gray

# ---------------------------------------------------------------------------
# 3. per fork: scan ALL branches, compare against upstream base
# ---------------------------------------------------------------------------
$rows = New-Object System.Collections.ArrayList
$n = 0
foreach ($f in $forks) {
    $n++
    $login = $f.login
    $full  = $f.full
    Write-Host ("[{0}/{1}] {2}" -f $n, $forks.Count, $login) -ForegroundColor DarkGray

    $row = [ordered]@{
        login        = $login
        full         = $full
        category     = ''
        ahead        = 0
        branches     = 0
        firstTouch   = ''
        lastTouch    = ''
        issueCount   = 0
        prCount      = 0
        prNumbers    = ''
        authors      = ''
        forkCreated  = ([string]$f.created).Substring(0,10)
        note         = ''
    }

    $accessible = $true
    $branchNames = @()
    try {
        $branchNames = Invoke-GhRawLines @('api', "repos/$full/branches?per_page=100", '--paginate', '--jq', '.[].name')
    } catch {
        $accessible = $false
        $row.note = 'branches API failed (deleted/private/empty)'
    }

    if ($accessible) {
        if ($MaxBranchesPerFork -gt 0 -and $branchNames.Count -gt $MaxBranchesPerFork) {
            $branchNames = $branchNames[0..($MaxBranchesPerFork-1)]
            $row.note = "branch scan capped at $MaxBranchesPerFork"
        }
        $row.branches = $branchNames.Count

        $seenSha   = @{}
        $dates     = New-Object System.Collections.ArrayList
        $authorSet = @{}
        $anyCompareOk = $false

        foreach ($b in $branchNames) {
            $b = "$b".Trim()
            if (-not $b) { continue }
            try {
                # @() forces an array: a single-element return from the helper would
                # otherwise be unrolled to a bare object whose .Count is $null.
                $cmp = @(Invoke-GhJson @('api', "repos/$Upstream/compare/$BaseBranch...${login}:$b", '--jq',
                    '{ahead:.ahead_by, commits:[.commits[] | {sha:.sha, name:.commit.author.name, email:.commit.author.email, date:.commit.author.date}]}'))
                $anyCompareOk = $true
                if ($cmp.Count -gt 0 -and $cmp[0].commits) {
                    foreach ($c in $cmp[0].commits) {
                        if (-not $seenSha.ContainsKey($c.sha)) {
                            $seenSha[$c.sha] = $true
                            [void]$dates.Add([datetime]$c.date)
                            $id = "$($c.name) <$($c.email)>"
                            $authorSet[$id] = $true
                        }
                    }
                }
            } catch {
                # individual branch compare can fail (no common history); skip it
            }
        }

        if (-not $anyCompareOk -and $branchNames.Count -gt 0) {
            $accessible = $false
            if (-not $row.note) { $row.note = 'all branch compares failed' }
        }

        $row.ahead   = $seenSha.Keys.Count
        $row.authors = ($authorSet.Keys | Sort-Object) -join ' ; '
        if ($dates.Count -gt 0) {
            $sorted = $dates | Sort-Object
            $row.firstTouch = $sorted[0].ToString('yyyy-MM-dd')
            $row.lastTouch  = $sorted[-1].ToString('yyyy-MM-dd')
        }
    }

    # PR / issue cross-reference (by login, case-insensitive)
    $lk = "$login".ToLower()
    if ($prsByUser.ContainsKey($lk)) {
        $row.prCount   = $prsByUser[$lk].Count
        $row.prNumbers = (($prsByUser[$lk] | ForEach-Object {
            $st = if ($_.merged) { 'merged' } else { $_.state }
            "#$($_.num)($st)"
        }) -join ' ')
    }
    if ($issuesByUser.ContainsKey($lk)) {
        $row.issueCount = $issuesByUser[$lk].Count
    }

    # classify
    if (-not $accessible) {
        $row.category = 'INACCESSIBLE'
    } elseif ($row.prCount -gt 0) {
        $row.category = 'PR_SHARED'
    } elseif ($row.ahead -gt 0) {
        $row.category = 'CHANGED_LOCAL'
    } elseif ($row.issueCount -gt 0) {
        $row.category = 'ISSUE_ONLY'
    } else {
        $row.category = 'PURE_FORK'
    }

    [void]$rows.Add([pscustomobject]$row)
}

# ---------------------------------------------------------------------------
# 4. write reports (markdown + csv) — local only
# ---------------------------------------------------------------------------
if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$stamp   = Get-Date -Format 'yyyy-MM-dd'
$mdPath  = Join-Path $OutDir "fork-audit-$stamp.md"
$csvPath = Join-Path $OutDir "fork-audit-$stamp.csv"

# CSV
$rows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

# category counts
$order = 'PR_SHARED','CHANGED_LOCAL','ISSUE_ONLY','PURE_FORK','INACCESSIBLE'
$counts = @{}
foreach ($o in $order) { $counts[$o] = @($rows | Where-Object { $_.category -eq $o }).Count }

# shared-account detection: >=2 distinct author identities in one fork
$shared = @($rows | Where-Object { $_.authors -and @($_.authors -split ' ; ').Count -ge 2 })
# login<->author mismatch: login not found inside any author identity string
$mismatch = @($rows | Where-Object {
    $_.authors -and ($_.ahead -gt 0) -and
    -not (($_.authors).ToLower().Contains(($_.login).ToLower()))
})

function Sort-Rows {
    param($all)
    $rank = @{ PR_SHARED=0; CHANGED_LOCAL=1; ISSUE_ONLY=2; PURE_FORK=3; INACCESSIBLE=4 }
    $all | Sort-Object @{e={$rank[$_.category]}}, @{e={[int]$_.ahead}; Descending=$true}, login
}

$sb = New-Object System.Text.StringBuilder
function Add-Line { param($t='') [void]$sb.AppendLine($t) }

Add-Line "# Fork 逆向盤點報告 — $Upstream"
Add-Line ""
Add-Line "- 產出時間：$(Get-Date -Format 'yyyy-MM-dd HH:mm')"
Add-Line "- 上游預設分支：``$BaseBranch``"
$gap = $ForkCount - $rows.Count
Add-Line "- Fork 總數：可列舉 **$($rows.Count)** 個（API forks_count=$ForkCount；差額 $gap 個推測已刪除／設為私有，無法列舉）"
Add-Line "- 上游 PR：**$($prs.Count)**（作者 $($prsByUser.Keys.Count) 人）｜真 issue：**$($issues.Count)**（作者 $($issuesByUser.Keys.Count) 人）"
Add-Line "- gh API 呼叫次數：$script:ghCalls"
Add-Line ""
Add-Line "## 分類摘要"
Add-Line ""
Add-Line "| 分類 | 數量 | 說明 |"
Add-Line "|---|---:|---|"
Add-Line "| PR_SHARED | $($counts['PR_SHARED']) | 有提 PR 想分享（最高價值） |"
Add-Line "| CHANGED_LOCAL | $($counts['CHANGED_LOCAL']) | 有改（commit ahead）但沒提 PR |"
Add-Line "| ISSUE_ONLY | $($counts['ISSUE_ONLY']) | 沒改但提了 issue |"
Add-Line "| PURE_FORK | $($counts['PURE_FORK']) | 純 fork，沒推任何更動 |"
Add-Line "| INACCESSIBLE | $($counts['INACCESSIBLE']) | fork 已刪／私有／空 |"
Add-Line "| **合計** | **$($rows.Count)** | 應等於 fork 總數 |"
Add-Line ""

Add-Line "## 特殊身分偵測"
Add-Line ""
if ($shared.Count -gt 0) {
    Add-Line "### 共用帳號（同一 fork 出現 ≥2 組 commit 作者身分）"
    Add-Line ""
    Add-Line "| login | 作者身分 |"
    Add-Line "|---|---|"
    foreach ($r in $shared) { Add-Line "| $($r.login) | $($r.authors) |" }
    Add-Line ""
} else {
    Add-Line "（未偵測到共用帳號）"
    Add-Line ""
}
if ($mismatch.Count -gt 0) {
    Add-Line "### login 與 commit 作者名不一致（避免漏算）"
    Add-Line ""
    Add-Line "| login | 作者身分 |"
    Add-Line "|---|---|"
    foreach ($r in $mismatch) { Add-Line "| $($r.login) | $($r.authors) |" }
    Add-Line ""
}

Add-Line "## 主總表"
Add-Line ""
Add-Line "| login | 分類 | ahead | 分支 | 第一次動 | 最近一次動 | issue | PR | PR 明細 | 作者身分 |"
Add-Line "|---|---|---:|---:|---|---|---:|---:|---|---|"
foreach ($r in (Sort-Rows $rows)) {
    Add-Line ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |" -f `
        $r.login, $r.category, $r.ahead, $r.branches, $r.firstTouch, $r.lastTouch,
        $r.issueCount, $r.prCount, $r.prNumbers, $r.authors)
}
Add-Line ""

# PR_SHARED merge-recommendation block
$prRows = $rows | Where-Object { $_.category -eq 'PR_SHARED' }
if ($prRows.Count -gt 0) {
    Add-Line "## 可併入建議（PR_SHARED）"
    Add-Line ""
    Add-Line "| login | ahead | PR 明細 |"
    Add-Line "|---|---:|---|"
    foreach ($r in ($prRows | Sort-Object @{e={[int]$_.ahead};Descending=$true})) {
        Add-Line "| $($r.login) | $($r.ahead) | $($r.prNumbers) |"
    }
    Add-Line ""
}

[System.IO.File]::WriteAllText($mdPath, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

# ---------------------------------------------------------------------------
# 5. console summary + self-consistency check
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "==== Fork Audit Summary ====" -ForegroundColor Green
foreach ($o in $order) { Write-Host ("  {0,-14} {1}" -f $o, $counts[$o]) }
$sum = ($counts.Values | Measure-Object -Sum).Sum
Write-Host ("  {0,-14} {1}" -f 'TOTAL', $sum)
if ($sum -ne $rows.Count) { Write-Host "  [WARN] total != row count" -ForegroundColor Red }
Write-Host ""
Write-Host "Report : $mdPath" -ForegroundColor Green
Write-Host "CSV    : $csvPath" -ForegroundColor Green
Write-Host "gh API calls: $script:ghCalls" -ForegroundColor Gray
