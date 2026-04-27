<#
.SYNOPSIS
    Revit MCP 部署前環境檢查腳本
.DESCRIPTION
    在部署 Revit MCP 之前，驗證所有必要的軟體、路徑、版本是否正確。
    任何 AI Agent 都可以先執行此腳本來確認環境狀態。
.NOTES
    用法：powershell -ExecutionPolicy Bypass -File scripts/preflight-check.ps1
    可選參數：-RevitVersion 2024（指定要檢查的 Revit 版本，預設自動偵測）
#>

param(
    [string]$RevitVersion = ""
)

$ErrorActionPreference = "Continue"
$passed = 0
$warned = 0
$failed = 0

function Get-CommandPathOrNull {
    param([string]$Name)
    try {
        $cmd = Get-Command $Name -ErrorAction Stop
        return $cmd.Source
    }
    catch {
        return $null
    }
}

function Write-Check {
    param([string]$Name, [string]$Status, [string]$Detail = "")
    switch ($Status) {
        "PASS" {
            Write-Host "  [PASS] " -ForegroundColor Green -NoNewline
            Write-Host "$Name" -NoNewline
            if ($Detail) { Write-Host " — $Detail" -ForegroundColor Gray } else { Write-Host "" }
            $script:passed++
        }
        "WARN" {
            Write-Host "  [WARN] " -ForegroundColor Yellow -NoNewline
            Write-Host "$Name" -NoNewline
            if ($Detail) { Write-Host " — $Detail" -ForegroundColor Yellow } else { Write-Host "" }
            $script:warned++
        }
        "FAIL" {
            Write-Host "  [FAIL] " -ForegroundColor Red -NoNewline
            Write-Host "$Name" -NoNewline
            if ($Detail) { Write-Host " — $Detail" -ForegroundColor Red } else { Write-Host "" }
            $script:failed++
        }
    }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Revit MCP Preflight Check" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ===== 1. Project Structure =====
Write-Host "[1/8] Project Structure" -ForegroundColor White

$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $projectRoot) { $projectRoot = (Get-Location).Path }

$requiredPaths = @(
    @{ Path = "MCP/RevitMCP.csproj"; Desc = "C# 專案檔" },
    @{ Path = "MCP/RevitMCP.addin"; Desc = "Addin 清單" },
    @{ Path = "MCP-Server/package.json"; Desc = "MCP Server 套件定義" },
    @{ Path = "MCP-Server/tsconfig.json"; Desc = "TypeScript 設定" },
    @{ Path = "CLAUDE.md"; Desc = "統一行為指引" },
    @{ Path = "domain"; Desc = "Domain 知識庫" }
)

foreach ($item in $requiredPaths) {
    $fullPath = Join-Path $projectRoot $item.Path
    if (Test-Path $fullPath) {
        Write-Check $item.Desc "PASS" $item.Path
    } else {
        Write-Check $item.Desc "FAIL" "找不到 $($item.Path)"
    }
}

Write-Host ""

# ===== 2. Node.js =====
Write-Host "[2/8] Node.js" -ForegroundColor White

$nodeVersion = $null
$nodeExe = Get-CommandPathOrNull -Name "node"
if (-not $nodeExe) {
    $nodeCandidates = @(
        (Join-Path $env:ProgramFiles "nodejs\node.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\nodejs\node.exe")
    )
    $nodeExe = $nodeCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
}

try {
    if ($nodeExe) {
        $nodeVersion = (& $nodeExe --version 2>$null)
    }

    if ($nodeVersion) {
        $major = [int]($nodeVersion -replace 'v','').Split('.')[0]
        if ($major -ge 20) {
            Write-Check "Node.js 版本" "PASS" "$nodeVersion (需要 >= 20)"
        } else {
            Write-Check "Node.js 版本" "FAIL" "$nodeVersion 過舊，需要 >= 20 LTS"
        }
    }
} catch {
    Write-Check "Node.js" "FAIL" "未安裝。請至 https://nodejs.org 下載 LTS 版本"
}

$npmVersion = $null
$npmExe = Get-CommandPathOrNull -Name "npm"
if (-not $npmExe -and $nodeExe) {
    $nodeDir = Split-Path -Parent $nodeExe
    $npmCandidate = Join-Path $nodeDir "npm.cmd"
    if (Test-Path $npmCandidate) {
        $npmExe = $npmCandidate
    }
}

try {
    if ($npmExe) {
        $npmVersion = (& $npmExe --version 2>$null)
    }

    if ($npmVersion) {
        Write-Check "npm" "PASS" "v$npmVersion"
    }
} catch {
    Write-Check "npm" "FAIL" "未安裝（通常隨 Node.js 一起安裝）"
}

Write-Host ""

# ===== 3. .NET SDK =====
Write-Host "[3/8] .NET SDK" -ForegroundColor White

$hasFramework48 = $false
$hasNet8 = $false

try {
    $dotnetExe = Get-CommandPathOrNull -Name "dotnet"
    if (-not $dotnetExe) {
        $dotnetCandidates = @(
            (Join-Path $env:ProgramFiles "dotnet\dotnet.exe"),
            (Join-Path $env:USERPROFILE ".dotnet\dotnet.exe")
        )
        $dotnetExe = $dotnetCandidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    }

    $dotnetVersions = $null
    if ($dotnetExe) {
        $dotnetVersions = (& $dotnetExe --list-sdks 2>$null)
    }

    if ($dotnetVersions) {
        Write-Check "dotnet CLI" "PASS" "可用"

        foreach ($line in $dotnetVersions) {
            if ($line -match "^8\.") { $hasNet8 = $true }
        }

        if ($hasNet8) {
            Write-Check ".NET 8 SDK" "PASS" "Revit 2025-2026 可用"
        } else {
            Write-Check ".NET 8 SDK" "WARN" "未安裝（僅影響 Revit 2025-2026 建構）"
        }
    }
} catch {
    Write-Check "dotnet CLI" "FAIL" "未安裝。請至 https://dotnet.microsoft.com/download 下載"
}

# Check .NET Framework 4.8 (Windows registry)
$frameworkKey = "HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"
if (Test-Path $frameworkKey) {
    $release = (Get-ItemProperty $frameworkKey -ErrorAction SilentlyContinue).Release
    if ($release -ge 528040) {
        $hasFramework48 = $true
        Write-Check ".NET Framework 4.8" "PASS" "Revit 2022-2024 可用"
    } else {
        Write-Check ".NET Framework 4.8" "WARN" "版本過舊（Release=$release，需要 >= 528040）"
    }
} else {
    Write-Check ".NET Framework 4.8" "WARN" "無法偵測（非 Windows 或未安裝）"
}

Write-Host ""

# ===== 4. Port 8964 =====
Write-Host "[4/8] Port 8964 (WebSocket)" -ForegroundColor White

try {
    $portCheck = netstat -ano 2>$null | Select-String ":8964 "
    if ($portCheck) {
        $pid = ($portCheck -split '\s+')[-1]
        $processName = (Get-Process -Id $pid -ErrorAction SilentlyContinue).ProcessName
        Write-Check "Port 8964" "WARN" "已被 $processName (PID: $pid) 佔用。關閉該程式或改 port"
    } else {
        Write-Check "Port 8964" "PASS" "未被佔用"
    }
} catch {
    Write-Check "Port 8964" "PASS" "無法檢查（可能正常）"
}

Write-Host ""

# ===== 5. Revit Installation =====
Write-Host "[5/8] Revit Add-ins 資料夾" -ForegroundColor White

$addinsBase = Join-Path $env:APPDATA "Autodesk\Revit\Addins"
$supportedVersions = @("2020", "2021", "2022", "2023", "2024", "2025", "2026")
$detectedVersions = @()
$unsupportedDetectedVersions = @()

if (Test-Path $addinsBase) {
    $versionFolders = Get-ChildItem $addinsBase -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match "^20\d\d$" }

    foreach ($folder in $versionFolders) {
        if ($folder.Name -in $supportedVersions) {
            $detectedVersions += $folder.Name
            Write-Check "Revit $($folder.Name) Addins 資料夾" "PASS" $folder.FullName
        }
        else {
            $unsupportedDetectedVersions += $folder.Name
            Write-Check "Revit $($folder.Name) Addins 資料夾" "WARN" "已偵測到，但目前腳本僅支援部署 2020-2026"
        }
    }

    if ($detectedVersions.Count -eq 0 -and $unsupportedDetectedVersions.Count -eq 0) {
        Write-Check "Revit Addins" "WARN" "找到 Addins 根目錄但無 Revit 版本資料夾（Revit 可能未安裝）"
    }
} else {
    Write-Check "Revit Addins 資料夾" "WARN" "不存在：$addinsBase（Revit 可能未安裝在此電腦）"
}

Write-Host ""

# ===== 6. MCP Server Build =====
Write-Host "[6/8] MCP Server 建構狀態" -ForegroundColor White

$indexJs = Join-Path $projectRoot "MCP-Server/build/index.js"
$nodeModules = Join-Path $projectRoot "MCP-Server/node_modules"

if (Test-Path $nodeModules) {
    Write-Check "node_modules" "PASS" "已安裝"
} else {
    Write-Check "node_modules" "FAIL" "未安裝。請執行：cd MCP-Server && npm install"
}

if (Test-Path $indexJs) {
    $buildTime = (Get-Item $indexJs).LastWriteTime
    Write-Check "build/index.js" "PASS" "已編譯（$buildTime）"
} else {
    Write-Check "build/index.js" "FAIL" "未編譯。請執行：cd MCP-Server && npm run build"
}

Write-Host ""

# ===== 7. Revit Add-in Build =====
Write-Host "[7/8] Revit Add-in 建構狀態" -ForegroundColor White

$dllCandidates = @(
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R20/RevitMCP.dll"; Label = "Release.R20" },
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R21/RevitMCP.dll"; Label = "Release.R21" },
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R22/RevitMCP.dll"; Label = "Release.R22" },
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R23/RevitMCP.dll"; Label = "Release.R23" },
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R24/RevitMCP.dll"; Label = "Release.R24" },
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R25/RevitMCP.dll"; Label = "Release.R25" },
    @{ Path = Join-Path $projectRoot "MCP/bin/Release.R26/RevitMCP.dll"; Label = "Release.R26" }
)

$builtDlls = @($dllCandidates | Where-Object { Test-Path $_.Path })

if ($builtDlls.Count -gt 0) {
    foreach ($dll in $builtDlls) {
        $buildTime = (Get-Item $dll.Path).LastWriteTime
        Write-Check "RevitMCP.dll ($($dll.Label))" "PASS" "已建構（$buildTime）"
    }
} else {
    Write-Check "RevitMCP.dll" "FAIL" "未建構。請執行：cd MCP && dotnet build -c Release.R{YY} RevitMCP.csproj"
}

# Check if DLL is deployed to any Revit version
$deployedTo = @()
foreach ($ver in $detectedVersions) {
    $deployedDll = Join-Path $addinsBase "$ver\RevitMCP\RevitMCP.dll"
    if (Test-Path $deployedDll) {
        $deployedTo += $ver
    }
}

if ($deployedTo.Count -gt 0) {
    Write-Check "已部署到 Revit" "PASS" "版本：$($deployedTo -join ', ')"
} elseif ($detectedVersions.Count -gt 0) {
    Write-Check "已部署到 Revit" "WARN" "DLL 尚未部署到任何 Revit 版本"
}

if ($unsupportedDetectedVersions.Count -gt 0) {
    Write-Check "不在支援矩陣的版本" "WARN" "已偵測：$($unsupportedDetectedVersions -join ', ')（目前僅支援部署 2020-2026）"
}

Write-Host ""

# ===== 8. AI Client Config =====
Write-Host "[8/8] AI Client 設定檔" -ForegroundColor White

# Claude Desktop
$claudeConfig = Join-Path $env:APPDATA "Claude\claude_desktop_config.json"
if (Test-Path $claudeConfig) {
    $content = Get-Content $claudeConfig -Raw -ErrorAction SilentlyContinue
    if ($content -match "revit-mcp") {
        Write-Check "Claude Desktop" "PASS" "已設定 revit-mcp"
    } else {
        Write-Check "Claude Desktop" "WARN" "設定檔存在但未設定 revit-mcp"
    }
} else {
    Write-Check "Claude Desktop" "WARN" "未找到設定檔（未安裝或未設定）"
}

# Gemini CLI
$geminiConfig = Join-Path $env:USERPROFILE ".gemini\settings.json"
if (Test-Path $geminiConfig) {
    $content = Get-Content $geminiConfig -Raw -ErrorAction SilentlyContinue
    if ($content -match "revit-mcp") {
        Write-Check "Gemini CLI" "PASS" "已設定 revit-mcp"
    } else {
        Write-Check "Gemini CLI" "WARN" "設定檔存在但未設定 revit-mcp"
    }
} else {
    Write-Check "Gemini CLI" "WARN" "未找到設定檔（未安裝或未設定）"
}

# VS Code
$vscodeConfig = Join-Path $projectRoot ".vscode/mcp.json"
if (Test-Path $vscodeConfig) {
    Write-Check "VS Code Copilot" "PASS" "已設定 .vscode/mcp.json"
} else {
    Write-Check "VS Code Copilot" "WARN" "未找到 .vscode/mcp.json"
}

Write-Host ""

# ===== Summary =====
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  PASS: $passed" -ForegroundColor Green
Write-Host "  WARN: $warned" -ForegroundColor Yellow
Write-Host "  FAIL: $failed" -ForegroundColor Red
Write-Host ""

if ($failed -eq 0 -and $warned -eq 0) {
    Write-Host "  All checks passed! Ready to use Revit MCP." -ForegroundColor Green
} elseif ($failed -eq 0) {
    Write-Host "  No critical failures. Review warnings above." -ForegroundColor Yellow
} else {
    Write-Host "  $failed critical issue(s) must be fixed before deployment." -ForegroundColor Red
    Write-Host ""
    Write-Host "  Quick fix commands:" -ForegroundColor White
    if (-not (Test-Path (Join-Path $projectRoot "MCP-Server/node_modules"))) {
        Write-Host "    cd MCP-Server && npm install && npm run build" -ForegroundColor Gray
    }
    if ($builtDlls.Count -eq 0) {
        Write-Host "    cd MCP && dotnet build -c Release.R24 RevitMCP.csproj" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Documentation: CLAUDE.md | Scripts: scripts/install-addon.ps1" -ForegroundColor DarkGray
Write-Host ""
