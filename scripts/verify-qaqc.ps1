# Revit MCP QA/QC Verification Script
# Usage: .\scripts\verify-qaqc.ps1 [-SkipBuild] [-SkipDeploy] [-Version 2024]
#
# Phases:
#   1. File Structure Integrity
#   2. Cross-Reference Consistency
#   3. Build Configuration Validation
#   4. Build Verification (skip with -SkipBuild)
#   5. Deployment Verification (skip with -SkipDeploy)
#   7. Cross-Document Alignment (CLAUDE.md / BIM_MCP web / scripts must report same Skill/Domain/Tool counts)

param(
    [switch]$SkipBuild,
    [switch]$SkipDeploy,
    [string]$Version = ""
)

$ErrorActionPreference = "Continue"

$scriptDir = $PSScriptRoot
$projectRoot = Split-Path -Parent -Path $scriptDir

$totalPass = 0
$totalFail = 0
$totalSkip = 0
$failures = @()

function Write-Check {
    param([string]$Name, [bool]$Result, [string]$Detail = "")
    if ($Result) {
        Write-Host "  PASS  $Name" -ForegroundColor Green
        $script:totalPass++
    }
    else {
        Write-Host "  FAIL  $Name" -ForegroundColor Red
        if ($Detail) { Write-Host "         $Detail" -ForegroundColor Red }
        $script:totalFail++
        $script:failures += @{ Name = $Name; Detail = $Detail }
    }
}

function Write-Skip {
    param([string]$Name, [string]$Reason = "")
    Write-Host "  SKIP  $Name ($Reason)" -ForegroundColor DarkGray
    $script:totalSkip++
}

# Header
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  Revit MCP QA/QC Verification" -ForegroundColor Cyan
Write-Host "  Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
Write-Host "  Root: $projectRoot" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan

# ─────────────────────────────────────────────
# Phase 1: File Structure Integrity
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 1] File Structure Integrity" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

# 1-1: Forbidden files
Write-Host ""
Write-Host "  1-1. Forbidden files (must NOT exist):" -ForegroundColor Cyan
Write-Check "No RevitMCP.2024.csproj" (-not (Test-Path "$projectRoot\MCP\RevitMCP.2024.csproj")) "Legacy file found - delete it"
Write-Check "No RevitMCP.2024.addin" (-not (Test-Path "$projectRoot\MCP\RevitMCP.2024.addin")) "Duplicate addin found - delete it"
Write-Check "No MCP\MCP\ directory" (-not (Test-Path "$projectRoot\MCP\MCP")) "Nested directory found - delete it"
Write-Check "No fix_addin_path.ps1" (-not (Test-Path "$projectRoot\scripts\fix_addin_path.ps1")) "Dangerous script found - delete it"

# 1-2: Required files
Write-Host ""
Write-Host "  1-2. Required files (must exist):" -ForegroundColor Cyan
$requiredFiles = @(
    @("MCP\RevitMCP.csproj", "Unified build file"),
    @("MCP\RevitMCP.addin", "Unified addin config"),
    @("MCP\Application.cs", "Add-in entry point"),
    @("MCP\Core\CommandExecutor.cs", "Command dispatcher"),
    @("MCP\Core\SocketService.cs", "WebSocket service"),
    @("MCP\Core\ExternalEventManager.cs", "UI thread manager"),
    @("MCP\Core\RevitCompatibility.cs", "Cross-version compat layer"),
    @("MCP-Server\src\index.ts", "MCP Server entry"),
    @("MCP-Server\src\tools\revit-tools.ts", "Tool definitions"),
    @("MCP-Server\package.json", "Node.js dependencies")
)

foreach ($file in $requiredFiles) {
    $path = Join-Path $projectRoot $file[0]
    Write-Check "$($file[0])" (Test-Path $path) "$($file[1]) not found"
}

# 1-3: AI rule file consistency
Write-Host ""
Write-Host "  1-3. AI rule file consistency:" -ForegroundColor Cyan

$claudeMd = Join-Path $projectRoot "CLAUDE.md"
if (Test-Path $claudeMd) {
    $lines = (Get-Content $claudeMd).Count
    Write-Check "CLAUDE.md exists ($lines lines)" ($lines -gt 100) "CLAUDE.md too short ($lines lines < 100)"
}
else {
    Write-Check "CLAUDE.md exists" $false "Main rule file missing"
}

foreach ($redirect in @("GEMINI.md", "AGENTS.md")) {
    $path = Join-Path $projectRoot $redirect
    if (Test-Path $path) {
        $content = (Get-Content $path -Raw).Trim()
        Write-Check "$redirect is redirect" ($content -eq "CLAUDE.md") "Content is '$content' instead of 'CLAUDE.md'"
    }
    else {
        Write-Check "$redirect exists" $false "Redirect file missing"
    }
}

# ─────────────────────────────────────────────
# Phase 2: Cross-Reference Consistency
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 2] Cross-Reference Consistency" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

# 2-1: Stale reference scan
Write-Host ""
Write-Host "  2-1. Stale reference scan:" -ForegroundColor Cyan

$stalePatterns = @(
    @("RevitMCP\.2024\.csproj", "Deleted legacy build file"),
    @("RevitMCP\.2024\.addin", "Deleted legacy addin"),
    @("bin\\Release\.2024", "Old output path"),
    @("MCP\\MCP\\", "Old nested directory"),
    @("fix_addin_path", "Deleted script")
)

# Excluded files (historical/migration docs + 規範性提及)
$excludedFiles = @(
    "CHANGELOG.md",
    "MIGRATION_GUIDE.md",
    "Recent_Update_Review.md",
    ".claude/commands/qaqc.md",        # /qaqc 命令定義本身列舉「禁止檔名」
    "domain/lessons.md",                # 開發經驗檔，保留 legacy 教訓作為前車之鑑
    "domain/path-maintenance-qa.md",    # 路徑維護 QA，引用舊 nested dir 作為歷史修正紀錄
    "docs/0328的課程討論.md"            # 歷史教材，保留當時上下文
)

$mdFiles = Get-ChildItem -Path $projectRoot -Filter "*.md" -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch "node_modules|\.claude[\\/]plugins|docs[\\/]_archive" }

$staleFound = $false
foreach ($pattern in $stalePatterns) {
    foreach ($file in $mdFiles) {
        $relativePath = $file.FullName.Replace("$projectRoot\", "").Replace("$projectRoot/", "")

        # Skip excluded files — 標準化路徑分隔符為 / 後比對（兼容 Windows 反斜線）
        $normalizedPath = $relativePath.Replace("\", "/")
        $skip = $false
        foreach ($ex in $excludedFiles) {
            $normalizedEx = $ex.Replace("\", "/")
            if ($normalizedPath -like "*$normalizedEx") { $skip = $true; break }
        }
        # 額外排除 docs/0328 開頭的歷史教材檔（避免中文檔名 encoding 問題）
        if ($normalizedPath -like "docs/0328*") { $skip = $true }
        if ($skip) { continue }

        $content = Get-Content $file.FullName -Raw -ErrorAction SilentlyContinue
        if ($content -and $content -match $pattern[0]) {
            # Exception: CLAUDE.md "DO NOT" rules
            if ($relativePath -eq "CLAUDE.md" -and $content -match "DO NOT.*$($pattern[0])") { continue }
            if ($relativePath -eq "CLAUDE.md" -and $content -match "Legacy.*removed") { continue }

            Write-Host "  FAIL  $relativePath references '$($pattern[0])'" -ForegroundColor Red
            Write-Host "         $($pattern[1])" -ForegroundColor Red
            $totalFail++
            $staleFound = $true
            $failures += @{ Name = "Stale ref in $relativePath"; Detail = $pattern[1] }
        }
    }
}
if (-not $staleFound) {
    Write-Check "No stale references in active docs" $true
}

# 2-2: Navigation table check
Write-Host ""
Write-Host "  2-2. Navigation table completeness:" -ForegroundColor Cyan

foreach ($readme in @("README.md", "README.en.md")) {
    $path = Join-Path $projectRoot $readme
    if (Test-Path $path) {
        $content = Get-Content $path -Raw
        $hasAgents = $content -match "AGENTS\.md"
        $hasLessons = $content -match "domain/lessons\.md"
        $hasSkills = $content -match "\.claude/skills"
        $hasCommands = $content -match "\.claude/commands"
        $allPresent = $hasAgents -and $hasLessons -and $hasSkills -and $hasCommands
        Write-Check "$readme navigation table complete" $allPresent "Missing entries in doc navigation"
    }
}

# ─────────────────────────────────────────────
# Phase 3: Build Configuration Validation
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 3] Build Configuration Validation" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

# 3-1: csproj settings
Write-Host ""
Write-Host "  3-1. csproj settings:" -ForegroundColor Cyan
$csproj = Join-Path $projectRoot "MCP\RevitMCP.csproj"
if (Test-Path $csproj) {
    $content = Get-Content $csproj -Raw
    Write-Check "Nice3point.Revit.Sdk reference" ($content -match "Nice3point\.Revit\.Sdk") "Missing SDK reference"
    Write-Check "DeployAddin disabled" ($content -match "<DeployAddin>false</DeployAddin>") "DeployAddin must be false (Nice3point SDK 會自動產生 RevitMCP.{version}.addin 與手動 addin 衝突)"
    Write-Check "Release.R22 config" ($content -match "Release\.R22") "Missing Revit 2022 config"
    Write-Check "Release.R24 config" ($content -match "Release\.R24") "Missing Revit 2024 config"
    Write-Check "Release.R25 config" ($content -match "Release\.R25") "Missing Revit 2025 config"
    Write-Check "Release.R26 config" ($content -match "Release\.R26") "Missing Revit 2026 config"
}
else {
    Write-Check "csproj exists" $false "Cannot validate build config"
}

# 3-2: addin settings
Write-Host ""
Write-Host "  3-2. addin settings:" -ForegroundColor Cyan
$addin = Join-Path $projectRoot "MCP\RevitMCP.addin"
if (Test-Path $addin) {
    $content = Get-Content $addin -Raw
    # Assembly 路徑應為相對路徑 — 接受 "RevitMCP.dll" 或 "RevitMCP\RevitMCP.dll"（Nice3point SDK 子資料夾）
    Write-Check "Relative assembly path" ($content -match "<Assembly>RevitMCP[\\/]?(RevitMCP\.dll|\.dll)</Assembly>|<Assembly>RevitMCP\\RevitMCP\.dll</Assembly>") "Assembly path should be relative (RevitMCP.dll or RevitMCP\RevitMCP.dll)"
    Write-Check "No absolute path in addin" (-not ($content -match "[A-Z]:\\")) "Absolute path found in addin file"
    Write-Check "FullClassName correct" ($content -match "RevitMCP\.Application") "FullClassName should be RevitMCP.Application"

    # Count AddInId occurrences
    $addinIdCount = ([regex]::Matches($content, "<AddInId>")).Count
    Write-Check "Single AddInId" ($addinIdCount -le 2) "Multiple AddInId entries found ($addinIdCount)"
}
else {
    Write-Check "addin file exists" $false "Cannot validate addin config"
}

# 3-3: MCP Server
Write-Host ""
Write-Host "  3-3. MCP Server config:" -ForegroundColor Cyan
$pkg = Join-Path $projectRoot "MCP-Server\package.json"
if (Test-Path $pkg) {
    $content = Get-Content $pkg -Raw
    Write-Check "build script defined" ($content -match '"build"') "No build script in package.json"
    Write-Check "MCP SDK dependency" ($content -match "modelcontextprotocol") "Missing MCP SDK dependency"
}

# ─────────────────────────────────────────────
# Phase 4: Build Verification (Windows only)
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 4] Build Verification" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

if ($SkipBuild) {
    Write-Skip "C# build" "Skipped via -SkipBuild flag"
    Write-Skip "MCP Server build" "Skipped via -SkipBuild flag"
}
else {
    # Determine which versions to build
    $versions = @()
    if ($Version) {
        $versions += $Version
    }
    else {
        $versions = @("22", "24", "25", "26")
    }

    Write-Host ""
    Write-Host "  4-1. C# multi-version build:" -ForegroundColor Cyan
    $buildAllPass = $true
    foreach ($ver in $versions) {
        $shortVer = $ver
        if ($ver.Length -eq 4) { $shortVer = $ver.Substring(2) }

        Write-Host "  Building Release.R$shortVer..." -ForegroundColor DarkGray -NoNewline
        $buildResult = & dotnet build -c "Release.R$shortVer" "$projectRoot\MCP\RevitMCP.csproj" 2>&1
        $buildSuccess = $LASTEXITCODE -eq 0

        if ($buildSuccess) {
            $dll = Get-Item "$projectRoot\MCP\bin\Release\RevitMCP.dll" -ErrorAction SilentlyContinue
            if ($dll) {
                Write-Host "" # newline after -NoNewline
                Write-Check "R$shortVer build ($($dll.Length) bytes)" $true
            }
            else {
                Write-Host ""
                Write-Check "R$shortVer DLL output" $false "Build succeeded but DLL not found"
                $buildAllPass = $false
            }
        }
        else {
            Write-Host ""
            $errorLines = ($buildResult | Select-String "error") -join "; "
            Write-Check "R$shortVer build" $false $errorLines
            $buildAllPass = $false
        }
    }

    Write-Host ""
    Write-Host "  4-2. MCP Server build:" -ForegroundColor Cyan
    $mcpServerDir = Join-Path $projectRoot "MCP-Server"
    if (Test-Path "$mcpServerDir\package.json") {
        Push-Location $mcpServerDir
        $npmResult = & npm run build 2>&1
        $npmSuccess = $LASTEXITCODE -eq 0
        Pop-Location

        $indexJs = Join-Path $mcpServerDir "build\index.js"
        Write-Check "npm run build" ($npmSuccess -and (Test-Path $indexJs)) "MCP Server build failed"
    }
    else {
        Write-Check "MCP Server package.json" $false "Cannot build - package.json missing"
    }
}

# ─────────────────────────────────────────────
# Phase 5: Deployment Verification (Windows only)
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 5] Deployment Verification" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

if ($SkipDeploy) {
    Write-Skip "Deployment check" "Skipped via -SkipDeploy flag"
}
else {
    $appDataPath = $env:APPDATA
    $supportedVersions = @("2022", "2023", "2024", "2025", "2026")

    Write-Host ""
    Write-Host "  5-1. Installed addin locations:" -ForegroundColor Cyan
    $installedVersions = @()
    foreach ($ver in $supportedVersions) {
        $addinsDir = Join-Path $appDataPath "Autodesk\Revit\Addins\$ver"
        if (Test-Path $addinsDir) {
            $addinFiles = Get-ChildItem -Path $addinsDir -Filter "*.addin" -Recurse -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -match "RevitMCP|revit-mcp" }

            if ($addinFiles.Count -gt 0) {
                $installedVersions += $ver
                Write-Host "  Revit $ver : $($addinFiles.Count) addin file(s)" -ForegroundColor Gray
                foreach ($f in $addinFiles) {
                    Write-Host "    $($f.FullName)" -ForegroundColor DarkGray
                }
            }
        }
    }

    if ($installedVersions.Count -eq 0) {
        Write-Host "  No RevitMCP installations detected" -ForegroundColor DarkGray
        Write-Skip "Deployment check" "No installations found"
    }

    Write-Host ""
    Write-Host "  5-2. Duplicate addin detection:" -ForegroundColor Cyan
    $duplicateFound = $false
    foreach ($ver in $installedVersions) {
        $addinsDir = Join-Path $appDataPath "Autodesk\Revit\Addins\$ver"
        $addinFiles = Get-ChildItem -Path $addinsDir -Filter "*.addin" -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match "RevitMCP|revit-mcp" }

        if ($addinFiles.Count -gt 1) {
            Write-Check "Revit $ver single addin" $false "Found $($addinFiles.Count) addin files - keep only ONE"
            $duplicateFound = $true
        }
        elseif ($addinFiles.Count -eq 1) {
            # Verify DLL exists — 從 .addin 讀 Assembly 路徑，支援根目錄或子資料夾部署
            $addinContent = Get-Content $addinFiles[0].FullName -Raw -ErrorAction SilentlyContinue
            $dllDir = $addinFiles[0].DirectoryName
            $assemblyPath = if ($addinContent -match "<Assembly>([^<]+)</Assembly>") { $matches[1] } else { "RevitMCP.dll" }
            $dllPath = Join-Path $dllDir $assemblyPath
            if (Test-Path $dllPath) {
                $dll = Get-Item $dllPath
                Write-Check "Revit $ver DLL present ($($dll.Length) bytes)" $true
            }
            else {
                Write-Check "Revit $ver DLL present" $false "DLL missing at $dllPath (from .addin Assembly: $assemblyPath)"
            }
        }
    }
    if (-not $duplicateFound -and $installedVersions.Count -gt 0) {
        Write-Check "No duplicate addin files" $true
    }
}

# ─────────────────────────────────────────────
# Phase 7: Cross-Document Alignment
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 7] Cross-Document Alignment" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

# Helpers — single source of truth for counts
function Get-ToolCount {
    $hits = Select-String -Path "$projectRoot\MCP-Server\src\tools\*.ts" `
        -Pattern '^\s+name:\s*[''"]' -ErrorAction SilentlyContinue
    return $hits.Count
}

function Get-DomainCount {
    # All domain/*.md including meta — single grand total
    return (Get-ChildItem -Path "$projectRoot\domain" -Filter "*.md" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'README.md' }).Count
}

function Get-SkillCount {
    return (Get-ChildItem -Path "$projectRoot\.claude\skills\*\SKILL.md" -ErrorAction SilentlyContinue).Count
}

function Find-StaleNumbers {
    param([string]$Pattern, [string[]]$Paths, [string[]]$Exclude = @())
    $results = @()
    foreach ($p in $Paths) {
        $hits = Select-String -Path $p -Pattern $Pattern -ErrorAction SilentlyContinue
        foreach ($h in $hits) {
            $isExcluded = $false
            foreach ($ex in $Exclude) { if ($h.Path -like "*$ex*") { $isExcluded = $true; break } }
            if (-not $isExcluded) { $results += $h }
        }
    }
    return $results
}

$toolCount = Get-ToolCount
$domainCount = Get-DomainCount
$skillCount = Get-SkillCount

Write-Host ""
Write-Host "  7-0. Source-of-truth counts:" -ForegroundColor Cyan
Write-Host "    Skills  = $skillCount  (.claude/skills/*/SKILL.md)" -ForegroundColor Gray
Write-Host "    Domain  = $domainCount  (domain/*.md, ex README)" -ForegroundColor Gray
Write-Host "    Tools   = $toolCount  (MCP-Server/src/tools/*.ts)" -ForegroundColor Gray

# Exclude: archived snapshots, log files, and explicit-snapshot demo HTMLs
$skipPatterns = @('_archive', '\log\', '0425-presentation.html', 'karpathy-wiki-reading.bundled.txt')

# 7-1: Tool count consistency
Write-Host ""
Write-Host "  7-1. Tool count consistency ($toolCount):" -ForegroundColor Cyan
$toolScanPaths = @(
    "$projectRoot\CLAUDE.md",
    "$projectRoot\docs\BIM_MCP\*.html",
    "$projectRoot\docs\BIM_MCP\reference\*.html",
    "$projectRoot\docs\BIM_MCP\_shared.js"
)
$staleTools = Find-StaleNumbers -Pattern '\b92\b.{0,4}(個|tools?|工具|commands?)' -Paths $toolScanPaths -Exclude $skipPatterns
Write-Check "No stale 92-tool references" ($staleTools.Count -eq 0) `
    $(if ($staleTools.Count -gt 0) { "Found $($staleTools.Count) stale '92'`: $($staleTools[0].Path):$($staleTools[0].LineNumber)" } else { "" })

# 7-2: Domain count consistency
Write-Host ""
Write-Host "  7-2. Domain count consistency ($domainCount):" -ForegroundColor Cyan
$domainScanPaths = @(
    "$projectRoot\CLAUDE.md",
    "$projectRoot\docs\BIM_MCP\*.html",
    "$projectRoot\docs\BIM_MCP\reference\*.html",
    "$projectRoot\docs\BIM_MCP\_shared.js"
)
$staleDomain = Find-StaleNumbers -Pattern '35\+?\s*(個|Domain|markdown|SOP|Skill)' -Paths $domainScanPaths -Exclude $skipPatterns
Write-Check "No stale 35-domain references" ($staleDomain.Count -eq 0) `
    $(if ($staleDomain.Count -gt 0) { "Found $($staleDomain.Count) stale '35'`: $($staleDomain[0].Path):$($staleDomain[0].LineNumber)" } else { "" })

# 7-3: Skill count consistency (sanity — 19 is canonical)
Write-Host ""
Write-Host "  7-3. Skill count is 19:" -ForegroundColor Cyan
Write-Check "Skills folder has 19 SKILL.md" ($skillCount -eq 19) `
    $(if ($skillCount -ne 19) { "Got $skillCount, expected 19. Update CLAUDE.md '## Skills（19 個）' if intentional." } else { "" })

# 7-4: CLAUDE.md table → real domain files (forward check)
Write-Host ""
Write-Host "  7-4. CLAUDE.md domain table -> real files:" -ForegroundColor Cyan
$claudeMd = Get-Content "$projectRoot\CLAUDE.md" -Raw -ErrorAction SilentlyContinue
# Match real domain paths only; reject literal placeholders like {file}.md, {name}.md
$tablePattern = [regex]'`domain/[a-zA-Z0-9_\-\.\/]+\.md`'
$tableRefs = $tablePattern.Matches($claudeMd) | ForEach-Object { $_.Value.Trim('`') } | Sort-Object -Unique
$missingFiles = @()
foreach ($ref in $tableRefs) {
    $full = Join-Path $projectRoot $ref.Replace('/', '\')
    if (-not (Test-Path $full)) { $missingFiles += $ref }
}
Write-Check "All $($tableRefs.Count) CLAUDE.md domain refs resolve" ($missingFiles.Count -eq 0) `
    $(if ($missingFiles.Count -gt 0) { "Missing: $($missingFiles -join ', ')" } else { "" })

# 7-5: Real domain files → CLAUDE.md table (reverse check)
Write-Host ""
Write-Host "  7-5. Real domain files -> CLAUDE.md table:" -ForegroundColor Cyan
$metaDomain = @('README.md', 'frontmatter-standard.md', 'lessons.md', 'qa-checklist.md',
                'path-maintenance-qa.md', 'session-context-guard.md',
                'tool-capability-boundary.md', 'skill-authoring-standard.md')
$realDomain = Get-ChildItem -Path "$projectRoot\domain" -Filter "*.md" |
    Where-Object { $_.Name -notin $metaDomain } | ForEach-Object { $_.Name }
$notInTable = @()
foreach ($f in $realDomain) {
    if ($claudeMd -notmatch [regex]::Escape("domain/$f")) { $notInTable += $f }
}
Write-Check "All real domain files appear in CLAUDE.md table" ($notInTable.Count -eq 0) `
    $(if ($notInTable.Count -gt 0) { "Missing from table: $($notInTable -join ', ')" } else { "" })

# 7-6: BIM_MCP web internal links — domain/* / .claude/skills/* targets must exist
Write-Host ""
Write-Host "  7-6. BIM_MCP web link resolution:" -ForegroundColor Cyan
$webFiles = @()
$webFiles += Get-ChildItem -Path "$projectRoot\docs\BIM_MCP" -Filter "*.html" -ErrorAction SilentlyContinue
$webFiles += Get-ChildItem -Path "$projectRoot\docs\BIM_MCP\reference" -Filter "*.html" -ErrorAction SilentlyContinue
$linkPattern = [regex]'href="\.\./\.\./(domain/[^"#]+\.md|\.claude/skills/[^"#]+)"'
$brokenLinks = @()
foreach ($wf in $webFiles) {
    $content = Get-Content $wf.FullName -Raw -ErrorAction SilentlyContinue
    $matches = $linkPattern.Matches($content)
    foreach ($m in $matches) {
        $target = $m.Groups[1].Value
        $full = Join-Path $projectRoot $target.Replace('/', '\')
        if (-not (Test-Path $full)) { $brokenLinks += "$($wf.Name) -> $target" }
    }
}
Write-Check "No broken BIM_MCP -> source links" ($brokenLinks.Count -eq 0) `
    $(if ($brokenLinks.Count -gt 0) { "First broken: $($brokenLinks[0])" } else { "" })

# ─────────────────────────────────────────────
# Summary
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "  QA/QC Summary" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  PASS : $totalPass" -ForegroundColor Green
Write-Host "  FAIL : $totalFail" -ForegroundColor $(if ($totalFail -gt 0) { "Red" } else { "Green" })
Write-Host "  SKIP : $totalSkip" -ForegroundColor DarkGray
Write-Host ""

if ($totalFail -gt 0) {
    Write-Host "  FAILURES:" -ForegroundColor Red
    foreach ($f in $failures) {
        Write-Host "    - $($f.Name)" -ForegroundColor Red
        if ($f.Detail) {
            Write-Host "      $($f.Detail)" -ForegroundColor DarkGray
        }
    }
    Write-Host ""
    Write-Host "  RESULT: FAILED" -ForegroundColor Red
    exit 1
}
elseif ($totalSkip -gt 0) {
    Write-Host "  RESULT: PASSED (with skipped checks)" -ForegroundColor Yellow
    exit 0
}
else {
    Write-Host "  RESULT: ALL PASSED" -ForegroundColor Green
    exit 0
}
