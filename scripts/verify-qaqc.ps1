# Revit MCP QA/QC Verification Script
# Usage: .\scripts\verify-qaqc.ps1 [-SkipBuild] [-SkipDeploy] [-Version 2024]
#
# Phases:
#   1. File Structure Integrity
#   2. Cross-Reference Consistency
#   3. Build Configuration Validation
#   4. Build Verification (skip with -SkipBuild)
#   5. Deployment Verification (skip with -SkipDeploy)
#   6. Domain Metadata and Shared SOP Quality
#   7. Cross-Document Alignment (CLAUDE.md / BIM_MCP web / scripts must report same Skill/Domain/Tool counts)
#   8. Document Audience and Encoding Hygiene

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
$totalWarn = 0
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

function Write-Warn {
    param([string]$Name, [string]$Detail = "")
    Write-Host "  WARN  $Name" -ForegroundColor Yellow
    if ($Detail) { Write-Host "         $Detail" -ForegroundColor DarkYellow }
    $script:totalWarn++
}

# Robust text reader — bypasses Get-Content parameter-binding quirks on some files
function Read-FileText {
    param([string]$Path)
    try {
        if (-not $Path -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
        return [System.IO.File]::ReadAllText($Path)
    } catch {
        return $null
    }
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
    @("MCP-Server\src\tools\index.ts", "Runtime tool registry"),
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
        $rawContent = Read-FileText $path
        $content = if ($rawContent) { $rawContent.Trim() } else { "" }
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
    @("bin\\Release\\RevitMCP\.dll", "Old unified output path; use bin\\Release.R{YY}\\RevitMCP.dll"),
    @("MCP\\MCP\\", "Old nested directory"),
    @("fix_addin_path", "Deleted script")
)

# Excluded files (historical/migration docs + 規範性提及)
$excludedFiles = @(
    "CHANGELOG.md",
    "MIGRATION_GUIDE.md",
    "Recent_Update_Review.md",
    ".claude/commands/qaqc.md",        # /qaqc 命令定義本身列舉「禁止檔名」
    "domain/qa-checklist.md",          # QA checklist intentionally lists forbidden legacy paths
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

        $content = Read-FileText $file.FullName
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
        $content = Read-FileText $path
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
    $content = Read-FileText $csproj
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
    $content = Read-FileText $addin
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
    $content = Read-FileText $pkg
    Write-Check "build script defined" ($content -match '"build"') "No build script in package.json"
    Write-Check "MCP SDK dependency" ($content -match "modelcontextprotocol") "Missing MCP SDK dependency"
}

# 3-4: Client config template portability — templates must use <YOUR_PROJECT_PATH>, never a hardcoded user path
Write-Host ""
Write-Host "  3-4. Client config template portability:" -ForegroundColor Cyan
$templateFiles = Get-ChildItem -Path "$projectRoot\MCP-Server\*_config.json" -ErrorAction SilentlyContinue
$nonPortable = @()
foreach ($tf in $templateFiles) {
    $content = Read-FileText $tf.FullName
    if ($content -and ($content -match '[A-Za-z]:[\\/]+Users[\\/]')) {
        $nonPortable += $tf.Name
    }
}
Write-Check "Config templates contain no hardcoded user paths" ($nonPortable.Count -eq 0) `
    $(if ($nonPortable.Count -gt 0) { "Hardcoded user path in: $($nonPortable -join ', '). Use <YOUR_PROJECT_PATH> placeholder." } else { "" })

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
            $dll = Get-Item "$projectRoot\MCP\bin\Release.R$shortVer\RevitMCP.dll" -ErrorAction SilentlyContinue
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
            $addinContent = Read-FileText $addinFiles[0].FullName
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
# Phase 6: Domain Metadata and Shared SOP Quality
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 6] Domain Metadata and Shared SOP Quality" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

$domainFiles = Get-ChildItem -Path "$projectRoot\domain" -Filter "*.md" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne "README.md" }

Write-Host ""
Write-Host "  6-1. Required frontmatter fields:" -ForegroundColor Cyan
$frontmatterFailures = @()
foreach ($df in $domainFiles) {
    $text = Read-FileText $df.FullName
    $rel = $df.FullName.Replace("$projectRoot\", "").Replace("\", "/")
    if (-not $text -or -not ($text -match '(?s)^---\s*\r?\n(.*?)\r?\n---')) {
        $frontmatterFailures += "$rel missing YAML frontmatter"
        continue
    }

    $fm = $matches[1]
    foreach ($required in @("name:", "description:", "metadata:", "version:", "updated:")) {
        if ($fm -notmatch "(?m)^\s*$([regex]::Escape($required))") {
            $frontmatterFailures += "$rel missing $required"
        }
    }
}
Write-Check "Domain frontmatter required fields present" ($frontmatterFailures.Count -eq 0) `
    $(if ($frontmatterFailures.Count -gt 0) { ($frontmatterFailures | Select-Object -First 10) -join "`n" } else { "" })

Write-Host ""
Write-Host "  6-2. Domain files remain shared, not English-only:" -ForegroundColor Cyan
$englishOnlyDomain = @()
foreach ($df in $domainFiles) {
    $text = Read-FileText $df.FullName
    if (-not $text) { continue }
    $rel = $df.FullName.Replace("$projectRoot\", "").Replace("\", "/")
    $hasCjk = $text -match '[\u4e00-\u9fff]'
    if (-not $hasCjk) { $englishOnlyDomain += $rel }
}
if ($englishOnlyDomain.Count -gt 0) {
    Write-Warn "Some Domain files appear English-only" (($englishOnlyDomain | Select-Object -First 10) -join "`n")
}
else {
    Write-Check "Domain files retain Chinese-readable content" $true
}

Write-Host ""
Write-Host "  6-3. Domain related references resolve:" -ForegroundColor Cyan
$brokenRelated = @()
foreach ($df in $domainFiles) {
    $text = Read-FileText $df.FullName
    if (-not $text) { continue }
    $rel = $df.FullName.Replace("$projectRoot\", "").Replace("\", "/")
    foreach ($m in ([regex]'domain/[a-zA-Z0-9_\-\.\/]+\.md').Matches($text)) {
        $target = $m.Value
        if ($target -match '^domain/(xxx|example)[a-zA-Z0-9_\-]*\.md$') { continue }
        $full = Join-Path $projectRoot $target.Replace('/', '\')
        if (-not (Test-Path -LiteralPath $full)) {
            $brokenRelated += "$rel -> $target"
        }
    }
}
Write-Check "Domain-local domain/*.md references resolve" ($brokenRelated.Count -eq 0) `
    $(if ($brokenRelated.Count -gt 0) { ($brokenRelated | Select-Object -First 10) -join "`n" } else { "" })

# ─────────────────────────────────────────────
# Phase 7: Cross-Document Alignment
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 7] Cross-Document Alignment" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

# Helpers — single source of truth for counts
function Get-ToolCount {
    $nodeScript = "import('./MCP-Server/build/tools/index.js').then(m=>{console.log(m.registerRevitTools().length)}).catch(()=>process.exit(2))"
    Push-Location $projectRoot
    $result = & node --input-type=module -e $nodeScript 2>$null
    $exit = $LASTEXITCODE
    Pop-Location
    if ($exit -eq 0 -and $result -match '^\d+$') {
        return [int]$result
    }

    Write-Warn "Runtime tool registry count unavailable" "Falling back to source regex count. Run npm run build if this is unexpected."
    $hits = Select-String -Path "$projectRoot\MCP-Server\src\tools\*.ts" `
        -Pattern '^\s+name:\s*[''"]' -ErrorAction SilentlyContinue
    return $hits.Count
}

function Get-DomainCount {
    # All domain/*.md including meta — single grand total
    $rootCount = (Get-ChildItem -Path "$projectRoot\domain" -Filter "*.md" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ne 'README.md' }).Count
    $refCount = (Get-ChildItem -Path "$projectRoot\domain\references" -Filter "*.md" -ErrorAction SilentlyContinue).Count
    return ($rootCount + $refCount)
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
Write-Host "    Domain  = $domainCount  (domain/*.md ex README + domain/references/*.md)" -ForegroundColor Gray
Write-Host "    Tools   = $toolCount  (runtime registerRevitTools())" -ForegroundColor Gray

# Exclude: archived snapshots, log files, immutable date-prefixed snapshot HTMLs, external bundled mirrors
# Snapshot policy: every docs/MMDD-*.html is an immutable event snapshot — its numbers reflect
# the event date and are never re-synced. Living documents (BIM_MCP reference) stay in scope.
$skipPatterns = @('_archive', '\log\', '\docs\0425-', '\docs\0523-', 'reference\external')

# Scan target files for claim-site checks (7-1/7-2/7-3)
$scanPaths = @(
    "$projectRoot\CLAUDE.md",
    "$projectRoot\README.md",
    "$projectRoot\README.en.md",
    "$projectRoot\docs\DOCUMENT_AUDIENCE_INVENTORY.md",
    "$projectRoot\docs\BIM_MCP\*.html",
    "$projectRoot\docs\BIM_MCP\reference\*.html",
    "$projectRoot\docs\BIM_MCP\_shared.js"
)

# Known claim-site patterns — ONLY match GRAND-TOTAL claim phrases (not "5 個 ARCHI 工具" type batch counts).
# Each: { Pattern (regex w/ 1 capture group) ; Truth ; Label }
# Truth note: Domain Knowledge heading + N Domain refs use $domainCount+1 because 1 entry is from domain/references/
$claimSites = @(
    # Markdown count-table claims (CLAUDE.md / README.md / README.en.md / DOCUMENT_AUDIENCE_INVENTORY.md)
    @{ Pattern = '\|\s*Runtime MCP tools\s*\|\s*(\d+)\s*\|';           Truth = $toolCount;          Label = '| Runtime MCP tools | N |' },
    @{ Pattern = '\|\s*Domain SOP files\s*\|\s*(\d+)\s*\|';            Truth = $domainCount;        Label = '| Domain SOP files | N |' },
    @{ Pattern = '\|\s*Claude skills\s*\|\s*(\d+)\s*\|';               Truth = $skillCount;         Label = '| Claude skills | N |' },
    # Tool count grand-total claims
    @{ Pattern = '共用\s*(\d+)\s*個工具';                              Truth = $toolCount;          Label = '共用 N 個工具' },
    @{ Pattern = '個\s*Domain[、，]\s*(\d+)\s*個工具';                  Truth = $toolCount;          Label = 'N 個工具 (hero 三層)' },
    @{ Pattern = '\((\d+)\+?\s*commands?\)';                           Truth = $toolCount;          Label = '(N+ commands)' },
    @{ Pattern = '\((\d+)\s+tools?,';                                  Truth = $toolCount;          Label = '(N tools, ...)' },
    @{ Pattern = '封裝\s*(\d+)\s*個\s*tools?';                          Truth = $toolCount;          Label = '封裝 N 個 tools' },
    @{ Pattern = '(\d+)\s*個\s*MCP\s*tools?\b';                        Truth = $toolCount;          Label = '個 MCP tools' },
    @{ Pattern = '(\d+)\s*個\s*原子工具';                              Truth = $toolCount;          Label = '個原子工具' },
    @{ Pattern = '(\d+)\s*個\s*語意化工具';                            Truth = $toolCount;          Label = '個語意化工具' },
    @{ Pattern = 'Tool[s]?[（(](\d+)[)）]';                             Truth = $toolCount;          Label = 'Tool（N）' },
    @{ Pattern = '「(\d+)\s*工具編排平台';                              Truth = $toolCount;          Label = '「N 工具編排平台」' },
    @{ Pattern = '警告：(\d+)\s*工具不該';                              Truth = $toolCount;          Label = '警告：N 工具不該' },
    @{ Pattern = '(\d+)\s*個工具可以組合';                              Truth = $toolCount;          Label = 'N 個工具可以組合' },
    # Domain count grand-total claims
    @{ Pattern = 'Domain Knowledge.{0,40}（(\d+)\s*個';                Truth = $domainCount; Label = 'Domain Knowledge 標題' },
    @{ Pattern = '(\d+)\+?\s*個?\s*Domain\b';                          Truth = $domainCount; Label = 'N Domain' },
    @{ Pattern = '(\d+)\s*個\s*SOP';                                   Truth = $domainCount; Label = '個 SOP' },
    @{ Pattern = '(\d+)\s*個\s*domain/\*\.md';                         Truth = $domainCount; Label = '個 domain/*.md' },
    @{ Pattern = '(\d+)\s*個\s*<code>domain';                          Truth = $domainCount; Label = '個 <code>domain' },
    # Skill count grand-total claims (must require explicit grand-total context)
    @{ Pattern = '##\s*Skills（(\d+)\s*個）';                           Truth = $skillCount;         Label = '## Skills（N 個）' },
    @{ Pattern = 'Skills\s*索引（(\d+)\s*個）';                         Truth = $skillCount;         Label = 'Skills 索引（N 個）' },
    @{ Pattern = '(\d+)\s*個編排層\s*Skill';                            Truth = $skillCount;         Label = 'N 個編排層 Skill' },
    @{ Pattern = '(\d+)\s*Skill\s*vs\b';                               Truth = $skillCount;         Label = 'N Skill vs ...' },
    @{ Pattern = 'Skill\s*=\s*編排（(\d+)\s*個';                        Truth = $skillCount;         Label = 'Skill = 編排（N 個' },
    @{ Pattern = 'SKILLS INDEX[^<]*<span[^>]*>(\d+)\s*個';              Truth = $skillCount;         Label = 'SKILLS INDEX eyebrow N 個' },
    @{ Pattern = '>(\d+)\s+Skills</h4>';                                Truth = $skillCount;         Label = 'hub card N Skills' }
)

# Resolve all paths (glob → file list)
$scanFiles = @()
foreach ($p in $scanPaths) {
    if ($p -match '\*') {
        $scanFiles += Get-ChildItem -Path $p -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
    } elseif (Test-Path -LiteralPath $p) {
        $scanFiles += $p
    }
}
# Apply skip filter
$scanFiles = $scanFiles | Where-Object {
    $f = $_
    -not ($skipPatterns | Where-Object { $f -like "*$_*" })
}

# 7-1/7-2/7-3 unified exact-match scanner
function Find-ClaimMismatches {
    param([array]$Files, [hashtable]$Site)
    $mismatches = @()
    foreach ($f in $Files) {
        $text = Read-FileText $f
        if (-not $text) { continue }
        $lines = $text -split "`r?`n"
        $rx = [regex]$Site.Pattern
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $matches = $rx.Matches($lines[$i])
            foreach ($m in $matches) {
                $claimed = [int]$m.Groups[1].Value
                if ($claimed -ne $Site.Truth) {
                    $rel = $f.Replace("$projectRoot\", "").Replace("\", "/")
                    $mismatches += "    $rel`:$($i+1)  '$($Site.Label)' claims $claimed, truth is $($Site.Truth)"
                }
            }
        }
    }
    return $mismatches
}

# 7-1: Tool count exact-match
Write-Host ""
Write-Host "  7-1. Tool count exact-match (truth = $toolCount):" -ForegroundColor Cyan
$toolSites = $claimSites | Where-Object { $_.Truth -eq $toolCount }
$toolMismatches = @()
foreach ($site in $toolSites) {
    $toolMismatches += Find-ClaimMismatches -Files $scanFiles -Site $site
}
Write-Check "All tool-count claims == $toolCount" ($toolMismatches.Count -eq 0) `
    $(if ($toolMismatches.Count -gt 0) { "$($toolMismatches.Count) mismatch(es). First:`n$($toolMismatches -join "`n" | Select-Object -First 1)`nRun script for full list." } else { "" })
if ($toolMismatches.Count -gt 0) { $toolMismatches | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow } }

# 7-2: Domain count exact-match
Write-Host ""
Write-Host "  7-2. Domain count exact-match (truth = $domainCount incl references):" -ForegroundColor Cyan
$domainSites = $claimSites | Where-Object { $_.Truth -eq $domainCount }
$domainMismatches = @()
foreach ($site in $domainSites) {
    $domainMismatches += Find-ClaimMismatches -Files $scanFiles -Site $site
}
Write-Check "All domain-count claims == $domainCount" ($domainMismatches.Count -eq 0) `
    $(if ($domainMismatches.Count -gt 0) { "$($domainMismatches.Count) mismatch(es)." } else { "" })
if ($domainMismatches.Count -gt 0) { $domainMismatches | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow } }

# 7-3: Skill count exact-match
Write-Host ""
Write-Host "  7-3. Skill count exact-match (truth = $skillCount):" -ForegroundColor Cyan
$skillSites = $claimSites | Where-Object { $_.Truth -eq $skillCount }
$skillMismatches = @()
foreach ($site in $skillSites) {
    $skillMismatches += Find-ClaimMismatches -Files $scanFiles -Site $site
}
Write-Check "All skill-count claims == $skillCount" ($skillMismatches.Count -eq 0) `
    $(if ($skillMismatches.Count -gt 0) { "$($skillMismatches.Count) mismatch(es)." } else { "" })
if ($skillMismatches.Count -gt 0) { $skillMismatches | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow } }

# 7-4: CLAUDE.md table → real domain files (forward check)
Write-Host ""
Write-Host "  7-4. CLAUDE.md domain table -> real files:" -ForegroundColor Cyan
$claudeMd = Read-FileText "$projectRoot\CLAUDE.md"
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
    $content = Read-FileText $wf.FullName
    $matches = $linkPattern.Matches($content)
    foreach ($m in $matches) {
        $target = $m.Groups[1].Value
        $full = Join-Path $projectRoot $target.Replace('/', '\')
        if (-not (Test-Path $full)) { $brokenLinks += "$($wf.Name) -> $target" }
    }
}
Write-Check "No broken BIM_MCP -> source links" ($brokenLinks.Count -eq 0) `
    $(if ($brokenLinks.Count -gt 0) { "First broken: $($brokenLinks[0])" } else { "" })

# 7-7: Local markdown-link rot lint
# Scans README.md / README.en.md / DOCS_STRUCTURE.md / domain/*.md / .claude/skills/*/SKILL.md
# for markdown links [text](path) where path is a local relative file. Each target must exist.
Write-Host ""
Write-Host "  7-7. Local markdown link rot lint:" -ForegroundColor Cyan

$linkScanFiles = @()
$linkScanFiles += "$projectRoot\CLAUDE.md"
$linkScanFiles += "$projectRoot\README.md"
$linkScanFiles += "$projectRoot\README.en.md"
$linkScanFiles += "$projectRoot\docs\DOCS_STRUCTURE.md"
$linkScanFiles += Get-ChildItem -Path "$projectRoot\domain" -Filter "*.md" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }
$linkScanFiles += Get-ChildItem -Path "$projectRoot\.claude\skills\*\SKILL.md" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName }

# Markdown link [text](path). Capture group 1 = path.
# Exclude: URLs (http://, https://, mailto:), in-page anchors (#...), Windows paths with drive letter
$mdLinkRx = [regex]'\[(?:[^\]]+)\]\(([^)\s]+?)\)'
$rotted = @()
$totalChecked = 0
foreach ($lf in $linkScanFiles) {
    $text = Read-FileText $lf
    if (-not $text) { continue }
    $relFile = $lf.Replace("$projectRoot\", "").Replace("\", "/")
    $fileDir = Split-Path -Parent $lf
    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++) {
        foreach ($m in $mdLinkRx.Matches($lines[$i])) {
            $target = $m.Groups[1].Value
            # Skip URLs, in-page anchors, mailto, image data URIs
            if ($target -match '^(https?:|mailto:|#|data:|ftp:)') { continue }
            # Skip if inside a code span — odd number of backticks before this match
            $before = $lines[$i].Substring(0, $m.Index)
            $backticksBefore = ($before.ToCharArray() | Where-Object { $_ -eq '`' }).Count
            if ($backticksBefore % 2 -eq 1) { continue }
            # Strip trailing #anchor / ?query
            $pathOnly = $target -replace '[#?].*$', ''
            if ([string]::IsNullOrWhiteSpace($pathOnly)) { continue }
            # Skip Windows-style absolute paths (drive letter) — unlikely in markdown but safe
            if ($pathOnly -match '^[A-Z]:[\\/]') { continue }
            $totalChecked++
            # Resolve relative path from the markdown file's directory
            $candidate = Join-Path $fileDir $pathOnly.Replace('/', '\')
            $resolved = $null
            try { $resolved = (Resolve-Path -LiteralPath $candidate -ErrorAction SilentlyContinue).Path } catch {}
            if (-not $resolved -or -not (Test-Path -LiteralPath $resolved)) {
                $rotted += "    ${relFile}:$($i+1)  -> $target"
            }
        }
    }
}
Write-Check "All $totalChecked local markdown links resolve" ($rotted.Count -eq 0) `
    $(if ($rotted.Count -gt 0) { "$($rotted.Count) broken link(s)" } else { "" })
if ($rotted.Count -gt 0) { $rotted | Select-Object -First 20 | ForEach-Object { Write-Host $_ -ForegroundColor DarkYellow } }

# 7-8: Snapshot banner — every date-prefixed docs/MMDD-*.html must declare itself an
# immutable snapshot via a data-snapshot="YYYY-MM-DD" attribute, so readers know its
# numbers are historical and QAQC count-sync intentionally skips it.
Write-Host ""
Write-Host "  7-8. Snapshot banner on date-prefixed HTML:" -ForegroundColor Cyan
$snapshotHtml = Get-ChildItem -Path "$projectRoot\docs\*.html" -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d{4}-' }
$missingBanner = @()
foreach ($sf in $snapshotHtml) {
    $content = Read-FileText $sf.FullName
    if (-not $content -or $content -notmatch 'data-snapshot="\d{4}-\d{2}-\d{2}"') {
        $missingBanner += $sf.Name
    }
}
Write-Check "All $($snapshotHtml.Count) date-prefixed HTMLs carry data-snapshot banner" ($missingBanner.Count -eq 0) `
    $(if ($missingBanner.Count -gt 0) { "Missing banner: $($missingBanner -join ', ')" } else { "" })

# ─────────────────────────────────────────────
# Phase 8: Document Audience and Encoding Hygiene
# ─────────────────────────────────────────────
Write-Host ""
Write-Host "[Phase 8] Document Audience and Encoding Hygiene" -ForegroundColor Yellow
Write-Host "─────────────────────────────────────────────" -ForegroundColor DarkGray

function Test-Mojibake {
    param([string]$Text)
    if (-not $Text) { return $false }
    return ($Text -match '�|嚗|銝|蝣|摰|撠|閬|瘜|憭|蝺|頝|瑼|雿')
}

Write-Host ""
Write-Host "  8-1. Audience inventory exists:" -ForegroundColor Cyan
$inventory = Join-Path $projectRoot "docs\DOCUMENT_AUDIENCE_INVENTORY.md"
Write-Check "docs/DOCUMENT_AUDIENCE_INVENTORY.md exists" (Test-Path -LiteralPath $inventory) "Document audience inventory missing"

Write-Host ""
Write-Host "  8-2. Canonical AI docs are English-oriented and mojibake-free:" -ForegroundColor Cyan
$canonicalAiDocs = @(
    "$projectRoot\CLAUDE.md",
    "$projectRoot\.claude\commands\qaqc.md"
)
$aiDocFailures = @()
foreach ($doc in $canonicalAiDocs) {
    $text = Read-FileText $doc
    $rel = $doc.Replace("$projectRoot\", "").Replace("\", "/")
    if (-not $text) {
        $aiDocFailures += "$rel missing or unreadable"
        continue
    }
    if (Test-Mojibake $text) { $aiDocFailures += "$rel contains mojibake-risk tokens" }
}
Write-Check "Canonical AI docs pass encoding check" ($aiDocFailures.Count -eq 0) `
    $(if ($aiDocFailures.Count -gt 0) { ($aiDocFailures | Select-Object -First 10) -join "`n" } else { "" })

Write-Host ""
Write-Host "  8-3. README docs are mojibake-free:" -ForegroundColor Cyan
$readmeFailures = @()
foreach ($doc in @("$projectRoot\README.md", "$projectRoot\README.en.md")) {
    $text = Read-FileText $doc
    $rel = $doc.Replace("$projectRoot\", "").Replace("\", "/")
    if (-not $text) {
        $readmeFailures += "$rel missing or unreadable"
        continue
    }
    if (Test-Mojibake $text) { $readmeFailures += "$rel contains mojibake-risk tokens" }
}
Write-Check "README.md and README.en.md pass encoding check" ($readmeFailures.Count -eq 0) `
    $(if ($readmeFailures.Count -gt 0) { ($readmeFailures | Select-Object -First 10) -join "`n" } else { "" })

Write-Host ""
Write-Host "  8-4. AI skill migration warning scan:" -ForegroundColor Cyan
$skillMojibake = @()
Get-ChildItem -Path "$projectRoot\.claude\skills\*\SKILL.md" -ErrorAction SilentlyContinue | ForEach-Object {
    $text = Read-FileText $_.FullName
    if ($text -and (Test-Mojibake $text)) {
        $skillMojibake += $_.FullName.Replace("$projectRoot\", "").Replace("\", "/")
    }
}
if ($skillMojibake.Count -gt 0) {
    Write-Warn "Some skill docs still need English/UTF-8 migration" (($skillMojibake | Select-Object -First 10) -join "`n")
}
else {
    Write-Check "Skill docs pass mojibake warning scan" $true
}

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
Write-Host "  WARN : $totalWarn" -ForegroundColor $(if ($totalWarn -gt 0) { "Yellow" } else { "Green" })
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
