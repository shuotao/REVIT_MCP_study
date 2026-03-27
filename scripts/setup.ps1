# ============================================================================
# Revit MCP 一鍵安裝程式（完整版）
# ============================================================================
# 此腳本會自動完成以下所有步驟：
#   1. 檢查並安裝必要軟體（Node.js、.NET SDK）
#   2. 編譯 MCP Server（npm install + npm run build）
#   3. 讓您選擇 Revit 版本（支援多選）
#   4. 為每個版本編譯並部署 Revit Add-in
#   5. 自動設定 AI 客戶端（Claude Desktop、Gemini CLI、VS Code）
#   6. Port 8964 預檢與自動釋放（HTTP.sys 孤兒清理）
# ============================================================================
# 使用方式：
#   初學者：雙擊 setup.bat 即可
#   進階者：powershell -ExecutionPolicy Bypass -File setup.ps1
#   AI Agent：powershell -ExecutionPolicy Bypass -File setup.ps1 -NonInteractive -RevitVersions "2024,2025"
# ============================================================================

param(
    [switch]$NonInteractive,
    [string]$RevitVersions = "",
    [switch]$SkipPrerequisites,
    [switch]$SkipMCPServer,
    [switch]$SkipRevitBuild,
    [switch]$SkipDeploy,
    [switch]$SkipAIConfig,
    [switch]$Help
)

#Requires -Version 5.1
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$OutputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# ============================================================================
# 全域變數
# ============================================================================
$script:projectRoot = $null
$script:results = @()
$script:totalSteps = 8
$script:currentStep = 0

# ============================================================================
# 輔助函式
# ============================================================================

function Write-Banner {
    Write-Host ""
    Write-Host "  ============================================================" -ForegroundColor Cyan
    Write-Host "     Revit MCP 一鍵安裝程式 v1.0" -ForegroundColor Cyan
    Write-Host "     從零開始，自動完成所有安裝步驟" -ForegroundColor Cyan
    Write-Host "  ============================================================" -ForegroundColor Cyan
    Write-Host ""
}

function Write-StepHeader {
    param([string]$Title)
    $script:currentStep++
    Write-Host ""
    Write-Host "  [$script:currentStep/$script:totalSteps] $Title" -ForegroundColor White
    Write-Host "  ------------------------------------------------------------" -ForegroundColor DarkGray
}

function Write-OK {
    param([string]$Message)
    Write-Host "    [OK] $Message" -ForegroundColor Green
}

function Write-Fail {
    param([string]$Message)
    Write-Host "    [!!] $Message" -ForegroundColor Red
}

function Write-Skip {
    param([string]$Message)
    Write-Host "    [--] $Message" -ForegroundColor DarkGray
}

function Write-Info {
    param([string]$Message)
    Write-Host "    $Message" -ForegroundColor Yellow
}

function Add-Result {
    param([string]$Name, [string]$Status, [string]$Detail = "")
    $script:results += [PSCustomObject]@{
        Name   = $Name
        Status = $Status
        Detail = $Detail
    }
}

function Test-SafePath {
    param([string]$Path, [string]$Description = "Path")
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    $dangerousPatterns = @('\.\.\\', '\.\.\/', '\$\(', '`', '\|', ';', '&', '<', '>')
    foreach ($p in $dangerousPatterns) {
        if ($Path -match $p) {
            Write-Fail "$Description contains unsafe characters"
            return $false
        }
    }
    return $true
}

function Refresh-PathEnv {
    $machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = "$userPath;$machinePath"
}

function Test-CommandAvailable {
    param([string]$Command)
    try {
        $null = Get-Command $Command -ErrorAction Stop
        return $true
    }
    catch {
        return $false
    }
}

function Get-NodeMajorVersion {
    try {
        $ver = (node --version 2>$null)
        if ($ver -match '^v(\d+)') { return [int]$Matches[1] }
    }
    catch {}
    return 0
}

function Test-IsConsoleHost {
    return ($Host.Name -eq 'ConsoleHost') -and (-not [Console]::IsInputRedirected)
}

# ============================================================================
# 顯示說明
# ============================================================================

if ($Help) {
    Write-Host @"

  Revit MCP Setup Script

  Usage:
    .\setup.ps1                                          # Interactive mode
    .\setup.ps1 -NonInteractive -RevitVersions "2024"    # AI agent mode
    .\setup.ps1 -SkipPrerequisites                       # Skip Node/dotnet install
    .\setup.ps1 -Help                                    # Show this help

  Parameters:
    -NonInteractive     Skip all prompts, use defaults
    -RevitVersions      Comma-separated versions: "2024,2025,2026"
    -SkipPrerequisites  Skip Node.js and .NET SDK checks
    -SkipMCPServer      Skip npm install and build
    -SkipRevitBuild     Skip dotnet build
    -SkipDeploy         Skip DLL deployment to Revit
    -SkipAIConfig       Skip AI client configuration
    -Help               Show this message

"@
    exit 0
}

# ============================================================================
# Phase 0: 初始化與環境驗證
# ============================================================================

Write-Banner

# 確認是 Windows
if ($env:OS -ne "Windows_NT") {
    Write-Fail "此安裝程式僅支援 Windows 系統"
    Write-Info "Revit 只在 Windows 上運行，因此 Revit MCP 也只能在 Windows 上安裝"
    Read-Host "按 Enter 結束"
    exit 1
}

# 找到專案根目錄
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    $scriptDir = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
}
$script:projectRoot = Split-Path -Parent -Path $scriptDir

if (-not (Test-SafePath -Path $script:projectRoot -Description "Project Root")) {
    Read-Host "按 Enter 結束"
    exit 1
}

$script:projectRoot = (Resolve-Path $script:projectRoot).Path
$mcpPath = Join-Path $script:projectRoot "MCP"
$mcpServerPath = Join-Path $script:projectRoot "MCP-Server"

# 驗證專案結構
if (-not (Test-Path $mcpPath) -or -not (Test-Path $mcpServerPath)) {
    Write-Fail "找不到 MCP 或 MCP-Server 資料夾"
    Write-Info "請確認此腳本位於專案的 scripts/ 資料夾中"
    Read-Host "按 Enter 結束"
    exit 1
}

Write-OK "專案目錄：$($script:projectRoot)"

# 驗證 APPDATA 並定義共用路徑
$appDataPath = $env:APPDATA
if ([string]::IsNullOrEmpty($appDataPath) -or -not (Test-Path $appDataPath)) {
    Write-Fail "APPDATA 環境變數異常，無法繼續"
    Read-Host "按 Enter 結束"
    exit 1
}

$addinsBase = Join-Path $appDataPath "Autodesk\Revit\Addins"

# 檢查 Revit 是否正在執行
$revitProcess = Get-Process -Name "Revit" -ErrorAction SilentlyContinue
if ($revitProcess) {
    Write-Host ""
    Write-Host "  !! 偵測到 Revit 正在執行 !!" -ForegroundColor Red
    Write-Host ""
    Write-Info "安裝過程中需要複製檔案到 Revit 的資料夾，"
    Write-Info "如果 Revit 正在執行，可能會導致檔案被鎖定而無法複製。"
    Write-Host ""
    if (-not $NonInteractive) {
        Write-Info "建議您先關閉 Revit，然後按 Enter 繼續。"
        Write-Info "或者直接按 Enter 嘗試繼續（可能會在部署步驟失敗）。"
        Read-Host "  按 Enter 繼續"
    }
    else {
        Write-Info "非互動模式：繼續安裝（部署步驟可能失敗）"
    }
}

# ============================================================================
# Phase 1: 檢查並安裝必要軟體
# ============================================================================

Write-StepHeader "檢查必要軟體"

if ($SkipPrerequisites) {
    Write-Skip "跳過（使用者指定 -SkipPrerequisites）"
    Add-Result "必要軟體" "SKIP" "使用者跳過"
}
else {
    # --- 檢查 winget ---
    $hasWinget = Test-CommandAvailable "winget"

    # --- Node.js ---
    $nodeMajor = Get-NodeMajorVersion
    if ($nodeMajor -ge 20) {
        $nodeVer = (node --version 2>$null)
        Write-OK "Node.js $nodeVer（已安裝，符合需求 >= v20）"
        Add-Result "Node.js" "OK" $nodeVer
    }
    elseif ($nodeMajor -gt 0) {
        Write-Fail "Node.js 版本過舊（v$nodeMajor），需要 v20 以上"
        if ($hasWinget) {
            Write-Info "正在透過 winget 更新 Node.js..."
            try {
                $wingetOutput = & winget install OpenJS.NodeJS.LTS --scope user --accept-source-agreements --accept-package-agreements 2>&1
                Refresh-PathEnv
                $newMajor = Get-NodeMajorVersion
                if ($newMajor -ge 20) {
                    Write-OK "Node.js 已更新至 v$newMajor"
                    Add-Result "Node.js" "OK" "Updated to v$newMajor"
                }
                else {
                    Write-Fail "更新後仍無法偵測到新版 Node.js"
                    Write-Info "請關閉此視窗，重新開啟後再執行一次 setup.bat"
                    Write-Info "（安裝成功但需要重新載入環境變數）"
                    Add-Result "Node.js" "WARN" "Installed, needs restart"
                }
            }
            catch {
                Write-Fail "winget 安裝失敗：$($_.Exception.Message)"
                Write-Info "請手動前往 https://nodejs.org 下載 LTS 版本"
                Add-Result "Node.js" "FAIL" "Install failed"
            }
        }
        else {
            Write-Info "請手動前往 https://nodejs.org 下載 LTS 版本（v20 以上）"
            Write-Info "安裝完成後，關閉此視窗重新執行 setup.bat"
            Add-Result "Node.js" "FAIL" "Version too old, no winget"
        }
    }
    else {
        # Node.js 完全未安裝
        Write-Fail "未偵測到 Node.js"
        if ($hasWinget) {
            Write-Info "正在透過 winget 安裝 Node.js LTS..."
            try {
                $wingetOutput = & winget install OpenJS.NodeJS.LTS --scope user --accept-source-agreements --accept-package-agreements 2>&1
                Refresh-PathEnv
                $newMajor = Get-NodeMajorVersion
                if ($newMajor -ge 20) {
                    Write-OK "Node.js v$newMajor 安裝完成"
                    Add-Result "Node.js" "OK" "Installed v$newMajor"
                }
                else {
                    Write-Info "Node.js 安裝完成，但需要重新載入環境變數"
                    Write-Info "請關閉此視窗，重新開啟後再執行一次 setup.bat"
                    Add-Result "Node.js" "WARN" "Installed, needs restart"
                }
            }
            catch {
                Write-Fail "winget 安裝失敗"
                Write-Info "請手動前往 https://nodejs.org 下載 LTS 版本"
                Add-Result "Node.js" "FAIL" "Install failed"
            }
        }
        else {
            Write-Host ""
            Write-Info "未找到 winget 工具，無法自動安裝"
            Write-Host ""
            Write-Host "    請依照以下步驟手動安裝 Node.js：" -ForegroundColor White
            Write-Host "    1. 開啟瀏覽器，前往 https://nodejs.org" -ForegroundColor White
            Write-Host "    2. 點擊綠色的「LTS」按鈕下載" -ForegroundColor White
            Write-Host "    3. 執行下載的安裝程式，一直按「Next」" -ForegroundColor White
            Write-Host "    4. 安裝完成後，關閉此視窗重新執行 setup.bat" -ForegroundColor White
            Write-Host ""
            Add-Result "Node.js" "FAIL" "Not installed, no winget"

            if (-not $NonInteractive) {
                Read-Host "  安裝 Node.js 後按 Enter 重試，或直接按 Enter 繼續（後續步驟可能失敗）"
                Refresh-PathEnv
                $retryMajor = Get-NodeMajorVersion
                if ($retryMajor -ge 20) {
                    Write-OK "偵測到 Node.js v$retryMajor"
                    # 更新結果
                    $script:results = $script:results | Where-Object { $_.Name -ne "Node.js" }
                    Add-Result "Node.js" "OK" "v$retryMajor"
                }
            }
        }
    }

    # --- .NET SDK ---
    $hasDotnet = Test-CommandAvailable "dotnet"
    if ($hasDotnet) {
        try {
            $sdkList = (dotnet --list-sdks 2>$null)
            $hasNet8 = $false
            if ($sdkList) {
                foreach ($line in $sdkList) {
                    if ($line -match "^8\.") { $hasNet8 = $true }
                }
            }
            Write-OK ".NET CLI 可用"
            if ($hasNet8) {
                Write-OK ".NET 8 SDK 已安裝（Revit 2025-2026 可用）"
            }
            else {
                Write-Info ".NET 8 SDK 未安裝（僅影響 Revit 2025/2026）"
            }
            Add-Result ".NET SDK" "OK" $(if ($hasNet8) { ".NET 8 available" } else { "No .NET 8" })
        }
        catch {
            Write-OK ".NET CLI 可用（無法列出 SDK 版本）"
            Add-Result ".NET SDK" "OK" "CLI available"
        }
    }
    else {
        Write-Fail "未偵測到 .NET SDK"
        if ($hasWinget) {
            Write-Info "正在透過 winget 安裝 .NET 8 SDK..."
            try {
                $wingetOutput = & winget install Microsoft.DotNet.SDK.8 --scope user --accept-source-agreements --accept-package-agreements 2>&1
                Refresh-PathEnv
                if (Test-CommandAvailable "dotnet") {
                    Write-OK ".NET 8 SDK 安裝完成"
                    Add-Result ".NET SDK" "OK" "Installed .NET 8"
                }
                else {
                    Write-Info ".NET SDK 安裝完成，但需要重新載入環境變數"
                    Write-Info "請關閉此視窗，重新開啟後再執行一次 setup.bat"
                    Add-Result ".NET SDK" "WARN" "Installed, needs restart"
                }
            }
            catch {
                Write-Fail "winget 安裝失敗"
                Write-Info "請前往 https://dotnet.microsoft.com/download 下載 .NET 8 SDK"
                Add-Result ".NET SDK" "FAIL" "Install failed"
            }
        }
        else {
            Write-Host ""
            Write-Host "    請依照以下步驟手動安裝 .NET SDK：" -ForegroundColor White
            Write-Host "    1. 開啟瀏覽器，前往 https://dotnet.microsoft.com/download" -ForegroundColor White
            Write-Host "    2. 下載 .NET 8 SDK" -ForegroundColor White
            Write-Host "    3. 執行安裝程式" -ForegroundColor White
            Write-Host "    4. 安裝完成後，關閉此視窗重新執行 setup.bat" -ForegroundColor White
            Write-Host ""
            Add-Result ".NET SDK" "FAIL" "Not installed, no winget"
        }
    }

    # 檢查是否有關鍵缺失，無法繼續
    $nodeOk = (Get-NodeMajorVersion) -ge 20
    $dotnetOk = Test-CommandAvailable "dotnet"
    if (-not $nodeOk -and -not $dotnetOk) {
        Write-Host ""
        Write-Fail "Node.js 和 .NET SDK 都無法使用，無法繼續安裝"
        Write-Info "請先安裝這兩個軟體，再重新執行 setup.bat"
        Read-Host "按 Enter 結束"
        exit 1
    }
}

# ============================================================================
# Phase 2: 選擇 Revit 版本
# ============================================================================

Write-StepHeader "選擇 Revit 版本"

$supportedVersions = @("2022", "2023", "2024", "2025", "2026")
$detectedVersions = @()
$selectedVersions = @()

# 偵測已安裝的 Revit 版本
foreach ($ver in $supportedVersions) {
    $testPath = Join-Path $addinsBase $ver
    if (Test-Path $testPath) {
        $detectedVersions += $ver
    }
}

if ($detectedVersions.Count -gt 0) {
    Write-OK "偵測到已安裝的 Revit 版本：$($detectedVersions -join ', ')"
}
else {
    Write-Info "未偵測到已安裝的 Revit（Addins 資料夾不存在）"
    Write-Info "您仍可手動選擇版本，安裝程式會自動建立資料夾"
}

if ($NonInteractive) {
    # 非互動模式
    if (-not [string]::IsNullOrWhiteSpace($RevitVersions)) {
        $selectedVersions = @($RevitVersions -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -in $supportedVersions })
    }
    else {
        $selectedVersions = @($detectedVersions)
    }
    if ($selectedVersions.Count -eq 0) {
        Write-Fail "未指定有效的 Revit 版本，且未偵測到已安裝版本"
        Write-Info "請使用 -RevitVersions 參數指定版本，例如：-RevitVersions `"2024,2025`""
        exit 1
    }
    Write-OK "已選擇版本：$($selectedVersions -join ', ')"
}
else {
    # 互動模式：嘗試用方向鍵選單，失敗則用文字輸入
    $useArrowMenu = (Test-IsConsoleHost)

    if ($useArrowMenu) {
        # 建立選項陣列
        $menuItems = @()
        foreach ($ver in $supportedVersions) {
            $detected = $ver -in $detectedVersions
            $menuItems += [PSCustomObject]@{
                Version  = $ver
                Detected = $detected
                Selected = $detected  # 預設選取已偵測到的版本
            }
        }

        Write-Host ""
        Write-Host "    操作方式：" -ForegroundColor White
        Write-Host "      上/下方向鍵  移動游標" -ForegroundColor Gray
        Write-Host "      空白鍵        勾選/取消勾選" -ForegroundColor Gray
        Write-Host "      Enter         確認選擇" -ForegroundColor Gray
        Write-Host ""

        $cursorIndex = 0
        $menuDone = $false
        $maxIterations = 500  # 防止無限迴圈

        # 記錄選單起始位置
        $menuStartY = [Console]::CursorTop

        # 初次繪製選單
        foreach ($i in 0..($menuItems.Count - 1)) {
            $item = $menuItems[$i]
            $check = if ($item.Selected) { "x" } else { " " }
            $indicator = if ($i -eq $cursorIndex) { " >> " } else { "    " }
            $suffix = if ($item.Detected) { "  (已偵測到)" } else { "" }
            $color = if ($i -eq $cursorIndex) { "Cyan" } else { "White" }
            Write-Host "$indicator[$check] Revit $($item.Version)$suffix" -ForegroundColor $color
        }

        $iterCount = 0
        [Console]::CursorVisible = $false

        try {
            while (-not $menuDone -and $iterCount -lt $maxIterations) {
                $iterCount++
                $key = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

                switch ($key.VirtualKeyCode) {
                    38 {
                        # Up arrow
                        if ($cursorIndex -gt 0) { $cursorIndex-- }
                    }
                    40 {
                        # Down arrow
                        if ($cursorIndex -lt ($menuItems.Count - 1)) { $cursorIndex++ }
                    }
                    32 {
                        # Spacebar - toggle
                        $menuItems[$cursorIndex].Selected = -not $menuItems[$cursorIndex].Selected
                    }
                    13 {
                        # Enter - confirm
                        $menuDone = $true
                    }
                }

                if (-not $menuDone) {
                    # 重繪選單
                    [Console]::SetCursorPosition(0, $menuStartY)
                    foreach ($i in 0..($menuItems.Count - 1)) {
                        $item = $menuItems[$i]
                        $check = if ($item.Selected) { "x" } else { " " }
                        $indicator = if ($i -eq $cursorIndex) { " >> " } else { "    " }
                        $suffix = if ($item.Detected) { "  (已偵測到)" } else { "" }
                        $color = if ($i -eq $cursorIndex) { "Cyan" } else { "White" }
                        # 清除整行再寫入（避免殘留字元）
                        $line = "$indicator[$check] Revit $($item.Version)$suffix"
                        $padded = $line.PadRight(50)
                        Write-Host $padded -ForegroundColor $color
                    }
                }
            }
        }
        finally {
            [Console]::CursorVisible = $true
        }

        $selectedVersions = @($menuItems | Where-Object { $_.Selected } | ForEach-Object { $_.Version })
    }
    else {
        # 文字輸入模式（ISE / 重定向 / 不支援方向鍵的環境）
        Write-Host ""
        Write-Host "    可選版本：$($supportedVersions -join ', ')" -ForegroundColor White
        if ($detectedVersions.Count -gt 0) {
            Write-Host "    已偵測到：$($detectedVersions -join ', ')" -ForegroundColor Green
        }
        Write-Host ""
        $inputVersions = Read-Host "    請輸入要安裝的版本（以逗號分隔，例如 2024,2025）"
        if ([string]::IsNullOrWhiteSpace($inputVersions)) {
            $selectedVersions = $detectedVersions
        }
        else {
            $selectedVersions = $inputVersions -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -in $supportedVersions }
        }
    }

    if ($selectedVersions.Count -eq 0) {
        Write-Host ""
        Write-Fail "未選擇任何 Revit 版本"
        Write-Info "請至少選擇一個版本"
        Read-Host "按 Enter 結束"
        exit 1
    }

    Write-Host ""
    Write-OK "已選擇：$($selectedVersions -join ', ')"
}

Add-Result "Revit 版本" "OK" ($selectedVersions -join ', ')

# ============================================================================
# Phase 3: 編譯 MCP Server
# ============================================================================

Write-StepHeader "編譯 MCP Server（Node.js）"

if ($SkipMCPServer) {
    Write-Skip "跳過（使用者指定 -SkipMCPServer）"
    Add-Result "MCP Server" "SKIP" "使用者跳過"
}
elseif (-not (Test-CommandAvailable "node") -or -not (Test-CommandAvailable "npm")) {
    Write-Fail "Node.js / npm 不可用，跳過此步驟"
    Write-Info "請先安裝 Node.js，再重新執行 setup.bat"
    Add-Result "MCP Server" "FAIL" "Node.js not available"
}
else {
    $mcpServerDir = Join-Path $script:projectRoot "MCP-Server"
    $nodeModulesPath = Join-Path $mcpServerDir "node_modules"
    $buildIndexPath = Join-Path (Join-Path $mcpServerDir "build") "index.js"

    $mcpServerOk = $true
    Push-Location $mcpServerDir
    try {
        # npm install（如果 node_modules 不存在）
        if (-not (Test-Path $nodeModulesPath)) {
            Write-Info "正在安裝 Node.js 相依套件（npm install）..."
            Write-Info "這可能需要 1-3 分鐘，請耐心等待..."
            $npmInstallResult = & npm install 2>&1
            $npmInstallExit = $LASTEXITCODE
            if ($npmInstallExit -ne 0) {
                Write-Fail "npm install 失敗"
                Write-Info "錯誤訊息：$($npmInstallResult | Select-Object -Last 5 | Out-String)"
                Add-Result "MCP Server" "FAIL" "npm install failed"
                $mcpServerOk = $false
            }
            else {
                Write-OK "npm install 完成"
            }
        }
        else {
            Write-OK "node_modules 已存在，跳過 npm install"
        }

        # npm run build
        if ($mcpServerOk) {
            Write-Info "正在編譯 MCP Server（npm run build）..."
            $npmBuildResult = & npm run build 2>&1
            $npmBuildExit = $LASTEXITCODE
            if ($npmBuildExit -ne 0) {
                Write-Fail "npm run build 失敗"
                Write-Info "錯誤訊息：$($npmBuildResult | Select-Object -Last 5 | Out-String)"
                Add-Result "MCP Server" "FAIL" "npm run build failed"
            }
            elseif (Test-Path $buildIndexPath) {
                Write-OK "MCP Server 編譯完成"
                Add-Result "MCP Server" "OK" "build/index.js created"
            }
            else {
                Write-Fail "編譯似乎成功但找不到 build/index.js"
                Add-Result "MCP Server" "FAIL" "build/index.js missing"
            }
        }
    }
    catch {
        Write-Fail "MCP Server 編譯過程中發生錯誤：$($_.Exception.Message)"
        Add-Result "MCP Server" "FAIL" $_.Exception.Message
    }
    finally {
        Pop-Location
    }
}

# ============================================================================
# Phase 4 + 5: 為每個版本編譯並部署 Revit Add-in
# ============================================================================

Write-StepHeader "編譯 Revit Add-in（C#）"

$skipBuild = $SkipRevitBuild
$skipDeployFlag = $SkipDeploy

if ($skipBuild -and $skipDeployFlag) {
    Write-Skip "跳過編譯與部署"
    Add-Result "Revit Add-in" "SKIP" "使用者跳過"
}
elseif (-not (Test-CommandAvailable "dotnet")) {
    Write-Fail ".NET SDK 不可用，跳過編譯"
    Write-Info "請先安裝 .NET SDK，再重新執行 setup.bat"
    Add-Result "Revit Add-in" "FAIL" "dotnet not available"
}
else {
    $mcpDir = Join-Path $script:projectRoot "MCP"
    $csprojPath = Join-Path $mcpDir "RevitMCP.csproj"
    $addinSource = Join-Path $mcpDir "RevitMCP.addin"

    if (-not (Test-Path $csprojPath)) {
        Write-Fail "找不到 RevitMCP.csproj"
        Add-Result "Revit Add-in" "FAIL" "csproj missing"
    }
    else {
        $versionConfigMap = @{
            "2022" = "Release.R22"
            "2023" = "Release.R23"
            "2024" = "Release.R24"
            "2025" = "Release.R25"
            "2026" = "Release.R26"
        }

        foreach ($ver in $selectedVersions) {
            $config = $versionConfigMap[$ver]
            if (-not $config) {
                Write-Fail "Revit $ver：不支援的版本"
                Add-Result "Revit $ver" "FAIL" "Unsupported version"
                continue
            }

            # --- Build ---
            if (-not $skipBuild) {
                Write-Info "正在編譯 Revit $ver（dotnet build -c $config）..."
                Push-Location $mcpDir
                try {
                    $buildOutput = & dotnet build -c $config RevitMCP.csproj 2>&1
                    $buildExit = $LASTEXITCODE
                    if ($buildExit -ne 0) {
                        Write-Fail "Revit $ver 編譯失敗"
                        $errorLines = $buildOutput | Select-String -Pattern "error" | Select-Object -First 3
                        foreach ($errLine in $errorLines) {
                            Write-Info "  $errLine"
                        }
                        Add-Result "Revit $ver" "FAIL" "Build failed"
                        continue
                    }
                    Write-OK "Revit $ver 編譯完成"
                }
                catch {
                    Write-Fail "Revit $ver 編譯例外：$($_.Exception.Message)"
                    Add-Result "Revit $ver" "FAIL" $_.Exception.Message
                    continue
                }
                finally {
                    Pop-Location
                }
            }

            # --- Deploy ---
            if (-not $skipDeployFlag) {
                $dllSource = Join-Path (Join-Path (Join-Path $mcpDir "bin") $config) "RevitMCP.dll"

                if (-not (Test-Path $dllSource)) {
                    Write-Fail "Revit $ver：找不到 RevitMCP.dll（編譯可能失敗）"
                    Add-Result "Revit $ver" "FAIL" "DLL not found"
                    continue
                }

                $targetBase = Join-Path $addinsBase $ver
                $targetDllDir = Join-Path $targetBase "RevitMCP"

                try {
                    # 建立目錄
                    if (-not (Test-Path $targetBase)) {
                        New-Item -ItemType Directory -Path $targetBase -Force | Out-Null
                    }
                    if (-not (Test-Path $targetDllDir)) {
                        New-Item -ItemType Directory -Path $targetDllDir -Force | Out-Null
                    }

                    # 清理殘黨：刪除舊版 .addin 檔案（如 RevitMCP.2024.addin）
                    $legacyAddins = Get-ChildItem -Path $targetBase -Filter "RevitMCP.*.addin" -ErrorAction SilentlyContinue
                    foreach ($legacy in $legacyAddins) {
                        Remove-Item -Path $legacy.FullName -Force -ErrorAction SilentlyContinue
                        Write-Info "已清理殘留 .addin：$($legacy.Name)"
                    }

                    # 複製 DLL
                    Copy-Item -Path $dllSource -Destination (Join-Path $targetDllDir "RevitMCP.dll") -Force -ErrorAction Stop

                    # 複製 .addin（唯一正規檔案）
                    Copy-Item -Path $addinSource -Destination (Join-Path $targetBase "RevitMCP.addin") -Force -ErrorAction Stop

                    # 複製 Newtonsoft.Json.dll（如果存在）
                    $jsonDll = Join-Path (Join-Path (Join-Path $mcpDir "bin") $config) "Newtonsoft.Json.dll"
                    if (Test-Path $jsonDll) {
                        Copy-Item -Path $jsonDll -Destination (Join-Path $targetDllDir "Newtonsoft.Json.dll") -Force -ErrorAction SilentlyContinue
                    }

                    # 複製 ClosedXML.dll（如果存在）
                    $closedXmlDll = Join-Path (Join-Path (Join-Path $mcpDir "bin") $config) "ClosedXML.dll"
                    if (Test-Path $closedXmlDll) {
                        Copy-Item -Path $closedXmlDll -Destination (Join-Path $targetDllDir "ClosedXML.dll") -Force -ErrorAction SilentlyContinue
                    }

                    Write-OK "Revit $ver 部署完成 -> $targetBase"
                    Add-Result "Revit $ver" "OK" "Build + Deploy"
                }
                catch {
                    Write-Fail "Revit $ver 部署失敗：$($_.Exception.Message)"
                    if ($_.Exception.Message -match "being used") {
                        Write-Info "檔案被鎖定，請關閉 Revit 後重試"
                    }
                    Add-Result "Revit $ver" "FAIL" "Deploy failed: $($_.Exception.Message)"
                }
            }
            else {
                Write-OK "Revit $ver 編譯完成（跳過部署）"
                Add-Result "Revit $ver" "OK" "Build only"
            }
        }
    }
}

# ============================================================================
# Phase 6: 設定 AI 客戶端
# ============================================================================

Write-StepHeader "設定 AI 客戶端"

if ($SkipAIConfig) {
    Write-Skip "跳過（使用者指定 -SkipAIConfig）"
    Add-Result "AI 設定" "SKIP" "使用者跳過"
}
else {
    $mcpServerIndexJs = Join-Path (Join-Path (Join-Path $script:projectRoot "MCP-Server") "build") "index.js"
    # 將路徑轉換為正斜線（JSON 相容）
    $indexJsPath = $mcpServerIndexJs -replace '\\', '/'

    if (-not (Test-Path $mcpServerIndexJs)) {
        Write-Info "MCP Server 尚未編譯（build/index.js 不存在），AI 設定將指向預期路徑"
    }

    # --- Claude Desktop ---
    $claudeConfigDir = Join-Path $env:APPDATA "Claude"
    $claudeConfigFile = Join-Path $claudeConfigDir "claude_desktop_config.json"

    if (Test-Path $claudeConfigDir) {
        try {
            $claudeConfig = $null
            if (Test-Path $claudeConfigFile) {
                $existingContent = Get-Content $claudeConfigFile -Raw -ErrorAction SilentlyContinue
                if (-not [string]::IsNullOrWhiteSpace($existingContent)) {
                    try {
                        $claudeConfig = $existingContent | ConvertFrom-Json
                    }
                    catch {
                        # 備份損壞的設定檔
                        $backupPath = "$claudeConfigFile.bak"
                        Copy-Item $claudeConfigFile $backupPath -Force
                        Write-Info "Claude Desktop 設定檔格式異常，已備份為 .bak"
                    }
                }
            }

            # 建立或更新設定
            if ($null -eq $claudeConfig) {
                $claudeConfig = [PSCustomObject]@{
                    mcpServers = [PSCustomObject]@{}
                }
            }

            # 確保 mcpServers 屬性存在
            if (-not ($claudeConfig.PSObject.Properties.Match('mcpServers').Count -gt 0)) {
                $claudeConfig | Add-Member -MemberType NoteProperty -Name 'mcpServers' -Value ([PSCustomObject]@{})
            }

            # 加入或更新 revit-mcp
            $revitMcpEntry = [PSCustomObject]@{
                command = "node"
                args    = @($indexJsPath)
            }

            if ($claudeConfig.mcpServers.PSObject.Properties.Match('revit-mcp').Count -gt 0) {
                $claudeConfig.mcpServers.'revit-mcp' = $revitMcpEntry
            }
            else {
                $claudeConfig.mcpServers | Add-Member -MemberType NoteProperty -Name 'revit-mcp' -Value $revitMcpEntry
            }

            $claudeConfigJson = $claudeConfig | ConvertTo-Json -Depth 10
            [System.IO.File]::WriteAllText($claudeConfigFile, $claudeConfigJson, [System.Text.Encoding]::UTF8)
            Write-OK "Claude Desktop 設定已寫入"
            Add-Result "Claude Desktop" "OK" $claudeConfigFile
        }
        catch {
            Write-Fail "Claude Desktop 設定失敗：$($_.Exception.Message)"
            Write-Info "您可以手動將以下內容加入 $claudeConfigFile ："
            Write-Host "    `"revit-mcp`": { `"command`": `"node`", `"args`": [`"$indexJsPath`"] }" -ForegroundColor Gray
            Add-Result "Claude Desktop" "FAIL" $_.Exception.Message
        }
    }
    else {
        Write-Skip "Claude Desktop 未安裝（$claudeConfigDir 不存在）"
        Add-Result "Claude Desktop" "SKIP" "Not installed"
    }

    # --- Gemini CLI ---
    $geminiConfigDir = Join-Path $env:USERPROFILE ".gemini"
    $geminiConfigFile = Join-Path $geminiConfigDir "settings.json"

    try {
        # 建立 .gemini 目錄（如果不存在）
        if (-not (Test-Path $geminiConfigDir)) {
            New-Item -ItemType Directory -Path $geminiConfigDir -Force | Out-Null
        }

        $geminiConfig = $null
        if (Test-Path $geminiConfigFile) {
            $existingContent = Get-Content $geminiConfigFile -Raw -ErrorAction SilentlyContinue
            if (-not [string]::IsNullOrWhiteSpace($existingContent)) {
                try {
                    $geminiConfig = $existingContent | ConvertFrom-Json
                }
                catch {
                    $backupPath = "$geminiConfigFile.bak"
                    Copy-Item $geminiConfigFile $backupPath -Force
                    Write-Info "Gemini CLI 設定檔格式異常，已備份為 .bak"
                }
            }
        }

        if ($null -eq $geminiConfig) {
            $geminiConfig = [PSCustomObject]@{
                mcpServers = [PSCustomObject]@{}
            }
        }

        if (-not ($geminiConfig.PSObject.Properties.Match('mcpServers').Count -gt 0)) {
            $geminiConfig | Add-Member -MemberType NoteProperty -Name 'mcpServers' -Value ([PSCustomObject]@{})
        }

        $revitMcpEntry = [PSCustomObject]@{
            command = "node"
            args    = @($indexJsPath)
        }

        if ($geminiConfig.mcpServers.PSObject.Properties.Match('revit-mcp').Count -gt 0) {
            $geminiConfig.mcpServers.'revit-mcp' = $revitMcpEntry
        }
        else {
            $geminiConfig.mcpServers | Add-Member -MemberType NoteProperty -Name 'revit-mcp' -Value $revitMcpEntry
        }

        $geminiConfigJson = $geminiConfig | ConvertTo-Json -Depth 10
        [System.IO.File]::WriteAllText($geminiConfigFile, $geminiConfigJson, [System.Text.Encoding]::UTF8)
        Write-OK "Gemini CLI 設定已寫入"
        Add-Result "Gemini CLI" "OK" $geminiConfigFile
    }
    catch {
        Write-Fail "Gemini CLI 設定失敗：$($_.Exception.Message)"
        Add-Result "Gemini CLI" "FAIL" $_.Exception.Message
    }

    # --- VS Code ---
    $vscodeConfigFile = Join-Path (Join-Path $script:projectRoot ".vscode") "mcp.json"
    if (Test-Path $vscodeConfigFile) {
        Write-OK "VS Code 設定已存在（.vscode/mcp.json）"
        Add-Result "VS Code" "OK" "Already configured"
    }
    else {
        Write-Skip "VS Code .vscode/mcp.json 不存在"
        Add-Result "VS Code" "SKIP" "File missing"
    }
}

# ============================================================================
# Phase 7: Port 預檢與釋放
# ============================================================================

Write-StepHeader "Port 預檢與釋放"

Write-Host "    檢查 Port 8964 是否可用..." -ForegroundColor White

$portInUse = $false
try {
    $listeners = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
    $portInUse = ($listeners | Where-Object { $_.Port -eq 8964 }).Count -gt 0
} catch {
    $portInUse = $false
}

if (-not $portInUse) {
    Write-OK "Port 8964 可用"
    Add-Result "Port 8964" "OK" "Available"
}
else {
    # 辨識佔用者
    $occupantPid = 0
    $occupantName = "unknown"
    $netstatOutput = netstat -ano 2>$null | Select-String ":8964 "
    foreach ($line in $netstatOutput) {
        $parts = ($line.ToString().Trim()) -split '\s+'
        if ($parts.Count -ge 5) {
            $pid = [int]$parts[-1]
            if ($pid -gt 0) {
                $occupantPid = $pid
                try { $occupantName = (Get-Process -Id $pid -ErrorAction SilentlyContinue).ProcessName } catch { }
                break
            }
        }
    }

    Write-Host "    Port 8964 被 $occupantName (PID: $occupantPid) 佔用" -ForegroundColor Yellow

    $released = $false

    # Case 1: node/revitmcp 殭屍進程 — 直接 kill
    if ($occupantName -match "node|revitmcp") {
        Write-Host "    正在結束殭屍進程 $occupantName..." -ForegroundColor Yellow
        try {
            Stop-Process -Id $occupantPid -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
            $released = -not (Test-Path function:Test-PortInUse) -or $true  # 簡化判斷
            # 重新檢查
            $listeners2 = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
            $released = ($listeners2 | Where-Object { $_.Port -eq 8964 }).Count -eq 0
        } catch { }
    }

    # Case 2: PID 4 (HTTP.sys 孤兒) — 重啟 HTTP 服務
    if (-not $released -and $occupantPid -eq 4) {
        Write-Host "    偵測到 HTTP.sys 孤兒 Request Queue（上次 Revit 異常關閉殘留）" -ForegroundColor Yellow

        $isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        if ($isAdmin) {
            Write-Host "    正在重啟 HTTP 服務以釋放 Port..." -ForegroundColor Yellow
            try {
                $null = net stop http /y 2>&1
                Start-Sleep -Seconds 1
                $null = net start http 2>&1
                Start-Sleep -Milliseconds 500
                $listeners3 = [System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners()
                $released = ($listeners3 | Where-Object { $_.Port -eq 8964 }).Count -eq 0
            } catch { }
        }
        else {
            Write-Host "    需要系統管理員權限。請以系統管理員身分重新執行 setup.bat，" -ForegroundColor Red
            Write-Host "    或手動執行: net stop http /y && net start http" -ForegroundColor Cyan
        }
    }

    if ($released) {
        Write-OK "Port 8964 已自動釋放"
        Add-Result "Port 8964" "OK" "Auto-released from $occupantName"
    }
    else {
        Write-Host "    [!!] Port 8964 釋放失敗" -ForegroundColor Red
        Write-Host "    手動修復: powershell -File scripts\release-port.ps1" -ForegroundColor Cyan
        Add-Result "Port 8964" "WARN" "Occupied by $occupantName (PID: $occupantPid)"
    }
}

Write-Host ""

# ============================================================================
# Phase 8: 安裝摘要
# ============================================================================

Write-StepHeader "安裝摘要"

Write-Host ""
Write-Host "  ============================================================" -ForegroundColor Cyan
Write-Host "     安裝結果" -ForegroundColor Cyan
Write-Host "  ============================================================" -ForegroundColor Cyan
Write-Host ""

$hasFailure = $false
foreach ($r in $script:results) {
    switch ($r.Status) {
        "OK" {
            Write-Host "    [OK] $($r.Name)" -ForegroundColor Green -NoNewline
            if ($r.Detail) { Write-Host " - $($r.Detail)" -ForegroundColor DarkGray } else { Write-Host "" }
        }
        "FAIL" {
            Write-Host "    [!!] $($r.Name)" -ForegroundColor Red -NoNewline
            if ($r.Detail) { Write-Host " - $($r.Detail)" -ForegroundColor DarkGray } else { Write-Host "" }
            $hasFailure = $true
        }
        "WARN" {
            Write-Host "    [??] $($r.Name)" -ForegroundColor Yellow -NoNewline
            if ($r.Detail) { Write-Host " - $($r.Detail)" -ForegroundColor DarkGray } else { Write-Host "" }
        }
        "SKIP" {
            Write-Host "    [--] $($r.Name)" -ForegroundColor DarkGray -NoNewline
            if ($r.Detail) { Write-Host " - $($r.Detail)" -ForegroundColor DarkGray } else { Write-Host "" }
        }
    }
}

Write-Host ""

if (-not $hasFailure) {
    Write-Host "  ============================================================" -ForegroundColor Green
    Write-Host "     安裝完成！" -ForegroundColor Green
    Write-Host "  ============================================================" -ForegroundColor Green
    Write-Host ""
    Write-Host "    接下來的步驟：" -ForegroundColor White
    Write-Host "    1. 完全關閉 Revit（如果正在執行）" -ForegroundColor White
    Write-Host "    2. 重新開啟 Revit" -ForegroundColor White
    Write-Host "    3. 應該看到「MCP Tools」面板" -ForegroundColor White
    Write-Host "    4. 點擊「MCP 服務 (開/關)」啟動服務" -ForegroundColor White
    Write-Host "    5. 開啟 Claude Desktop / Gemini CLI / VS Code" -ForegroundColor White
    Write-Host "    6. 開始用自然語言控制 Revit！" -ForegroundColor White
}
else {
    Write-Host "  ============================================================" -ForegroundColor Yellow
    Write-Host "     部分步驟失敗，請檢查上方標記 [!!] 的項目" -ForegroundColor Yellow
    Write-Host "  ============================================================" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "    修復後可以重新執行 setup.bat，已完成的步驟會自動跳過" -ForegroundColor White
}

Write-Host ""
Write-Host "    如需幫助：https://github.com/your-repo/REVIT_MCP/issues" -ForegroundColor DarkGray
Write-Host ""

if (-not $NonInteractive) {
    Read-Host "按 Enter 結束"
}
