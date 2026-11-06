# ============================================================================
# Revit MCP Add-in 自動安裝程式 (安全版本)
# ============================================================================
# 此指令稿會自動：
# 1. 偵測您的 Revit 版本
# 2. 自動編譯 C# 專案 (dotnet build)
# 3. 複製 RevitMCP.dll 和 RevitMCP.addin 到正確的資料夾
# ============================================================================
# 安全注意事項：
# - 此指令稿只從本機複製檔案，不會從網路下載
# - 不需要系統管理員權限（Add-in 目錄在使用者資料夾）
# - 所有路徑都經過驗證，防止路徑注入攻擊
# - 使用 Strict Mode 確保變數和錯誤處理
# ============================================================================

#Requires -Version 5.1
[CmdletBinding()]
param(
    # R1：明確指定目標 Revit 版本。未指定且偵測到多版本時，互動模式會詢問、
    # 非互動模式直接失敗——絕不擅自挑一個（舊版取「最高版本」，會把建構丟到別條作業線的 Revit）。
    [ValidateSet('2022', '2023', '2024', '2025', '2026')]
    [string]$Version,

    # R1：對所有「已安裝 Revit 且已有對應建構產物」的版本逐一部署。
    [switch]$All,

    # R5：非互動模式。不呼叫任何 Read-Host，以 exit code 表達結果（0 成功 / 非 0 失敗）。
    [switch]$NonInteractive,

    # 覆寫 Add-ins 根目錄（預設 $env:APPDATA\Autodesk\Revit\Addins）。
    # 存在的理由與 verify-qaqc.ps1 的同名參數相同：讓測試跑在暫存 fixture 上，不動使用者的實際部署。
    [string]$AddinsRoot = "",

    # R6：備份輪替，只保留最近 N 份 .bak，其餘自動清除。
    [int]$KeepBackups = 3
)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 非互動模式下，把所有「按 Enter 結束」收斂掉，避免在 CI／自動化流程中卡住。
function Wait-ForExit {
    param([int]$Code = 0)
    if (-not $NonInteractive) { Read-Host "按 Enter 結束" | Out-Null }
    exit $Code
}

# 設定編碼為 UTF-8 with BOM，解決中文亂碼問題
$OutputEncoding = [System.Text.Encoding]::UTF8

# ============================================================================
# 安全函式：驗證路徑是否安全
# ============================================================================
function Test-SafePath {
    param (
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string]$Description = "路徑"
    )
    
    # 檢查路徑是否為空
    if ([string]::IsNullOrWhiteSpace($Path)) {
        Write-Host "❌ 錯誤：$Description 為空" -ForegroundColor Red
        return $false
    }
    
    # 檢查路徑是否包含危險字元
    $dangerousPatterns = @(
        '\.\.\\',      # 路徑遍歷
        '\.\.\/',      # 路徑遍歷 (Unix 風格)
        '\$\(',        # 命令替換
        '`',           # PowerShell 跳脫字元
        '\|',          # 管線
        ';',           # 命令分隔
        '&',           # 命令連接
        '<',           # 重定向
        '>'            # 重定向
    )
    
    foreach ($pattern in $dangerousPatterns) {
        if ($Path -match $pattern) {
            Write-Host "❌ 錯誤：$Description 包含不安全字元" -ForegroundColor Red
            return $false
        }
    }
    
    return $true
}

# ============================================================================
# 安全函式：驗證檔案雜湊（可選）
# ============================================================================
function Get-FileHashInfo {
    param (
        [Parameter(Mandatory = $true)]
        [string]$FilePath
    )
    
    if (Test-Path $FilePath) {
        $hash = Get-FileHash -Path $FilePath -Algorithm SHA256
        return $hash.Hash
    }
    return $null
}

# ============================================================================
# 主程式開始
# ============================================================================

Write-Host ""
Write-Host "============================================================================" -ForegroundColor Cyan
Write-Host "   Revit MCP Add-in 自動安裝程式 (安全版本)" -ForegroundColor Cyan
Write-Host "============================================================================" -ForegroundColor Cyan
Write-Host ""

# ============================================================================
# 安全檢查 1：驗證執行環境
# ============================================================================

# 取得指令稿所在目錄
$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    $scriptDir = Split-Path -Parent -Path $MyInvocation.MyCommand.Definition
}

if (-not (Test-SafePath -Path $scriptDir -Description "Script Directory")) {
    Wait-ForExit 1
}

# 取得專案根目錄
$projectRoot = Split-Path -Parent -Path $scriptDir

if (-not (Test-Path $projectRoot)) {
    Write-Host "❌ 錯誤：無法確定專案目錄" -ForegroundColor Red
    Wait-ForExit 1
}

# 轉換為絕對路徑
$projectRoot = (Resolve-Path $projectRoot).Path

# 驗證專案結構
$mcpPath = Join-Path $projectRoot "MCP"
$mcpServerPath = Join-Path $projectRoot "MCP-Server"

if (-not (Test-Path $mcpPath)) {
    Write-Host "❌ 錯誤：找不到 MCP 資料夾" -ForegroundColor Red
    Write-Host "請確認您在 REVIT_MCP_study 專案目錄中執行此程式" -ForegroundColor Yellow
    Wait-ForExit 1
}

if (-not (Test-Path $mcpServerPath)) {
    Write-Host "❌ 錯誤：找不到 MCP-Server 資料夾" -ForegroundColor Red
    Write-Host "這可能不是正確的專案目錄" -ForegroundColor Yellow
    Wait-ForExit 1
}

Write-Host "✓ 專案目錄驗證通過：$projectRoot" -ForegroundColor Green
Write-Host ""

# ============================================================================
# 安全檢查 2：驗證 APPDATA 環境變數
# ============================================================================

$appDataPath = $env:APPDATA

if ([string]::IsNullOrEmpty($appDataPath)) {
    Write-Host "❌ 錯誤：APPDATA 環境變數未設定" -ForegroundColor Red
    Write-Host "這可能是系統設定問題，請聯繫技術支援" -ForegroundColor Yellow
    Wait-ForExit 1
}

if (-not (Test-SafePath -Path $appDataPath -Description "APPDATA")) {
    Wait-ForExit 1
}

if (-not (Test-Path $appDataPath)) {
    Write-Host "❌ 錯誤：APPDATA 路徑不存在：$appDataPath" -ForegroundColor Red
    Wait-ForExit 1
}

Write-Host "✓ 環境變數驗證通過" -ForegroundColor Green
Write-Host ""

# ============================================================================
# 版本 → 建構組態 / runtime 世代對應
# ============================================================================
# 統一建構：所有版本共用 MCP\RevitMCP.csproj + MCP\RevitMCP.addin。
# ⚠️ 禁止新增 RevitMCP.2024.csproj / RevitMCP.2024.addin 等版本特定檔案。
$versionConfigMap = [ordered]@{
    "2022" = "Release.R22"
    "2023" = "Release.R23"
    "2024" = "Release.R24"
    "2025" = "Release.R25"
    "2026" = "Release.R26"
}

# R3 防呆用：兩代 runtime 的相依組成不同。
# .NET Framework 4.8（R22-R24）需要 5 個相容 shim；.NET 8（R25-R26）由 runtime 內建，不應出現。
# 這是判斷「建構產物是否為該世代」最直接的特徵 —— 錯代部署在複製階段完全沒有徵兆，
# 只會在 Revit 載入或呼叫時才爆（RevitCompatibility.cs 的 IdType 在 Int32/Int64 間分歧）。
$netFxShims = @(
    'System.Buffers.dll',
    'System.IO.Packaging.dll',
    'System.Memory.dll',
    'System.Numerics.Vectors.dll',
    'System.Runtime.CompilerServices.Unsafe.dll'
)
$netFxVersions = @('2022', '2023', '2024')

# Add-ins 根目錄（-AddinsRoot 可覆寫，供測試用暫存 fixture）
$addinsBase = if ([string]::IsNullOrWhiteSpace($AddinsRoot)) {
    Join-Path $appDataPath "Autodesk\Revit\Addins"
}
else {
    $AddinsRoot
}

# ============================================================================
# 偵測已安裝的 Revit 版本
# ============================================================================

Write-Host "正在偵測已安裝的 Revit 版本..." -ForegroundColor Yellow
Write-Host ""

$foundVersions = @()
foreach ($v in $versionConfigMap.Keys) {
    if (Test-Path (Join-Path $addinsBase $v)) {
        Write-Host "  找到 Revit $v" -ForegroundColor Green
        $foundVersions += $v
    }
}
Write-Host ""

if ($foundVersions.Count -eq 0) {
    Write-Host "[錯誤] 沒有找到已安裝的 Revit" -ForegroundColor Red
    Write-Host "   檢查的路徑：$addinsBase\<year>" -ForegroundColor Yellow
    Write-Host "   支援的版本：2022、2023、2024、2025、2026" -ForegroundColor Yellow
    Wait-ForExit 1
}

# ============================================================================
# R1：決定要部署到哪些版本
# ============================================================================
# 舊版行為是「白名單由高到低取第一個存在的目錄」，等於總是挑最高版本，
# 而且那段 do/while 裡的 $userVersion 恆為非 null，永遠不會真的詢問使用者。
# 後果：把建構丟到使用者沒有意圖的 Revit（例如工作預設是 2024、2026 屬另一條作業線）。
# 現在：明確指定 > 互動詢問 > 非互動直接失敗。絕不擅自挑。

$targetVersions = @()

if ($All) {
    $targetVersions = $foundVersions
    Write-Host "模式：-All，將處理所有已安裝且有建構產物的版本" -ForegroundColor Cyan
}
elseif (-not [string]::IsNullOrWhiteSpace($Version)) {
    if ($foundVersions -notcontains $Version) {
        Write-Host "[錯誤] 指定的 Revit $Version 未安裝" -ForegroundColor Red
        Write-Host "   已安裝：$($foundVersions -join '、')" -ForegroundColor Yellow
        Wait-ForExit 1
    }
    $targetVersions = @($Version)
}
elseif ($foundVersions.Count -eq 1) {
    $targetVersions = $foundVersions
    Write-Host "只找到一個版本，將部署到 Revit $($foundVersions[0])" -ForegroundColor Cyan
}
else {
    if ($NonInteractive) {
        Write-Host "[錯誤] 偵測到多個 Revit 版本，但未指定 -Version，且處於 -NonInteractive 模式" -ForegroundColor Red
        Write-Host "   已安裝：$($foundVersions -join '、')" -ForegroundColor Yellow
        Write-Host "   請加上 -Version <年份> 或 -All" -ForegroundColor Yellow
        Wait-ForExit 1
    }
    Write-Host "找到多個 Revit 版本：$($foundVersions -join '、')" -ForegroundColor Cyan
    $answer = Read-Host "請輸入要部署的版本（或輸入 all 部署全部）"
    if ($answer -eq 'all') {
        $targetVersions = $foundVersions
    }
    elseif ($foundVersions -contains $answer) {
        $targetVersions = @($answer)
    }
    else {
        Write-Host "[錯誤] 無效的版本「$answer」" -ForegroundColor Red
        Wait-ForExit 1
    }
}

Write-Host ""

# ============================================================================
# 單一版本的部署程序
# ============================================================================
function Install-ToVersion {
    param(
        [Parameter(Mandatory = $true)][string]$RevitYear,
        [Parameter(Mandatory = $true)][string]$BuildConfig
    )

    Write-Host "------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " Revit $RevitYear  <-  $BuildConfig" -ForegroundColor Cyan
    Write-Host "------------------------------------------------------------" -ForegroundColor DarkGray

    $srcDir      = Join-Path $projectRoot "MCP\bin\$BuildConfig"
    $sourceAddin = Join-Path $projectRoot "MCP\RevitMCP.addin"
    $addonPath   = Join-Path $addinsBase $RevitYear
    $dllDestDir  = Join-Path $addonPath "RevitMCP"

    if (-not (Test-Path $srcDir)) {
        Write-Host "  [略過] 找不到建構產物 $srcDir" -ForegroundColor Yellow
        Write-Host "         先建構：dotnet build -c $BuildConfig MCP\RevitMCP.csproj" -ForegroundColor Gray
        return [pscustomobject]@{ Version = $RevitYear; Status = 'SKIP'; Reason = 'no build output' }
    }
    $srcDlls = @(Get-ChildItem -Path $srcDir -Filter '*.dll' -File -ErrorAction SilentlyContinue)
    if ($srcDlls.Count -eq 0) {
        Write-Host "  [略過] $srcDir 內沒有任何 DLL" -ForegroundColor Yellow
        return [pscustomobject]@{ Version = $RevitYear; Status = 'SKIP'; Reason = 'build output empty' }
    }
    if ($srcDlls.Name -notcontains 'RevitMCP.dll') {
        Write-Host "  [失敗] $srcDir 內找不到 RevitMCP.dll" -ForegroundColor Red
        return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = 'RevitMCP.dll missing' }
    }
    if (-not (Test-Path $sourceAddin)) {
        Write-Host "  [失敗] 找不到 $sourceAddin" -ForegroundColor Red
        return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = 'addin manifest missing' }
    }

    # --- R3：世代防呆（擋下 ABI 不相容的錯版部署）---
    $hasShims = @($srcDlls.Name | Where-Object { $netFxShims -contains $_ }).Count
    $expectFx = $netFxVersions -contains $RevitYear
    if ($expectFx -and $hasShims -eq 0) {
        Write-Host "  [中止] Revit $RevitYear 屬 .NET Framework 4.8 世代，應含 5 個相容 shim，實際 0 個" -ForegroundColor Red
        Write-Host "         $srcDir 看起來是 .NET 8（R25/R26）的產物；錯版部署只會在 Revit 載入時才失敗" -ForegroundColor Yellow
        Write-Host "         請重新建構：dotnet build -c $BuildConfig MCP\RevitMCP.csproj" -ForegroundColor Yellow
        return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = 'generation mismatch (expected .NET Framework, got .NET 8 layout)' }
    }
    if ((-not $expectFx) -and $hasShims -gt 0) {
        Write-Host "  [中止] Revit $RevitYear 屬 .NET 8 世代，不應含相容 shim，實際 $hasShims 個" -ForegroundColor Red
        Write-Host "         $srcDir 看起來是 .NET Framework 4.8（R22-R24）的產物" -ForegroundColor Yellow
        return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = 'generation mismatch (expected .NET 8, got .NET Framework layout)' }
    }
    $genName = if ($expectFx) { '.NET Framework 4.8' } else { '.NET 8' }
    Write-Host "  世代檢查通過（$genName，$($srcDlls.Count) 個 DLL）" -ForegroundColor Green

    foreach ($d in @($addonPath, $dllDestDir)) {
        if (-not (Test-Path $d)) {
            try { New-Item -ItemType Directory -Path $d -Force | Out-Null }
            catch {
                Write-Host "  [失敗] 無法建立 $d -- $_" -ForegroundColor Red
                return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = "cannot create $d" }
            }
        }
    }

    # --- R6：備份現行 DLL 並輪替 ---
    $currentDll = Join-Path $dllDestDir 'RevitMCP.dll'
    if (Test-Path $currentDll) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        try {
            Copy-Item -Path $currentDll -Destination (Join-Path $dllDestDir "RevitMCP.dll.bak-$stamp") -Force -ErrorAction Stop
            Write-Host "  已備份現行 DLL" -ForegroundColor Green
        }
        catch { Write-Host "  [警告] 備份失敗（不中止）-- $_" -ForegroundColor Yellow }

        $baks = @(Get-ChildItem -Path $dllDestDir -Filter 'RevitMCP.dll.bak*' -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending)
        if ($KeepBackups -ge 0 -and $baks.Count -gt $KeepBackups) {
            $toRemove = @($baks | Select-Object -Skip $KeepBackups)
            foreach ($b in $toRemove) {
                try { Remove-Item $b.FullName -Force -ErrorAction Stop } catch { }
            }
            Write-Host "  備份輪替：保留最近 $KeepBackups 份，清除 $($toRemove.Count) 份" -ForegroundColor Green
        }
    }

    # --- R2：複製建構產物的全部 DLL（不維護硬編白名單）---
    # 舊版只複製 RevitMCP.dll + Newtonsoft.Json.dll，其餘相依從不複製。
    # 實測後果：Revit 2026 長期缺 6 個相依，Excel 類工具必然 runtime 拋 FileNotFoundException。
    $copied = 0
    foreach ($f in $srcDlls) {
        try {
            Copy-Item -Path $f.FullName -Destination (Join-Path $dllDestDir $f.Name) -Force -ErrorAction Stop
            $copied++
        }
        catch {
            Write-Host "  [失敗] 無法複製 $($f.Name) -- $_" -ForegroundColor Red
            Write-Host "         常見原因：Revit 正在執行中（DLL 被鎖住），請關閉 Revit 後重試" -ForegroundColor Yellow
            return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = "copy failed: $($f.Name)" }
        }
    }
    try {
        Copy-Item -Path $sourceAddin -Destination (Join-Path $addonPath 'RevitMCP.addin') -Force -ErrorAction Stop
    }
    catch {
        Write-Host "  [失敗] 無法複製 RevitMCP.addin -- $_" -ForegroundColor Red
        return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = 'addin copy failed' }
    }
    Write-Host "  已複製 $copied 個 DLL + RevitMCP.addin" -ForegroundColor Green

    # --- R4：部署後逐檔 SHA256 驗證 ---
    $mismatch = @()
    foreach ($f in $srcDlls) {
        $t = Join-Path $dllDestDir $f.Name
        if (-not (Test-Path $t)) { $mismatch += "$($f.Name)（缺漏）"; continue }
        if ((Get-FileHash $f.FullName -Algorithm SHA256).Hash -ne (Get-FileHash $t -Algorithm SHA256).Hash) {
            $mismatch += "$($f.Name)（雜湊不符）"
        }
    }
    $addinSrcHash = (Get-FileHash $sourceAddin -Algorithm SHA256).Hash
    $addinDstHash = (Get-FileHash (Join-Path $addonPath 'RevitMCP.addin') -Algorithm SHA256).Hash
    if ($addinSrcHash -ne $addinDstHash) { $mismatch += 'RevitMCP.addin（雜湊不符）' }

    if ($mismatch.Count -gt 0) {
        Write-Host "  [失敗] 驗證不通過：$($mismatch.Count) 個檔案不符" -ForegroundColor Red
        foreach ($m in $mismatch) { Write-Host "         - $m" -ForegroundColor Yellow }
        return [pscustomobject]@{ Version = $RevitYear; Status = 'FAIL'; Reason = "verification failed ($($mismatch.Count) files)" }
    }
    Write-Host "  逐檔 SHA256 驗證通過（$($srcDlls.Count) DLL + manifest）" -ForegroundColor Green

    # --- 舊配置殘留提醒（#91 之前的根層 DLL）---
    $legacyRootDll = Join-Path $addonPath 'RevitMCP.dll'
    if (Test-Path $legacyRootDll) {
        Write-Host "  [提醒] 發現根層殘留 $legacyRootDll" -ForegroundColor Yellow
        Write-Host "         manifest 載入的是 RevitMCP\RevitMCP.dll，此檔為 #91 之前的舊配置遺留，可安全刪除" -ForegroundColor Gray
    }

    return [pscustomobject]@{ Version = $RevitYear; Status = 'OK'; Reason = "$($srcDlls.Count) DLL" }
}

# ============================================================================
# 執行部署
# ============================================================================
$results = @()
foreach ($v in $targetVersions) {
    $results += Install-ToVersion -RevitYear $v -BuildConfig $versionConfigMap[$v]
    Write-Host ""
}

# ============================================================================
# 共用資源：Python worker（與 Revit 版本無關，只需部署一次）
# ============================================================================
# DwgColumnExecutor.FindWorkerScript 會依 dll 同層 -> 開發樹 -> %APPDATA%\RevitMCP 尋找；
# 部署版的 dll 在 Add-ins 目錄，故 worker 須落在 %APPDATA%\RevitMCP 才找得到。
$sourceWorker = Join-Path $projectRoot "bridge\python\skills\ezdxf_worker.py"
if (Test-Path $sourceWorker) {
    $workerDir = Join-Path $appDataPath "RevitMCP"
    try {
        if (-not (Test-Path $workerDir)) { New-Item -ItemType Directory -Path $workerDir -Force | Out-Null }
        Copy-Item -Path $sourceWorker -Destination (Join-Path $workerDir "ezdxf_worker.py") -Force -ErrorAction Stop
        Write-Host "已部署 ezdxf_worker.py 到 $workerDir" -ForegroundColor Green
        Write-Host "  （柱號對應 textLayerName 需系統 Python + 'pip install ezdxf'；DWG 另需 ODA File Converter）" -ForegroundColor Gray
    }
    catch {
        Write-Host "[警告] 無法部署 ezdxf_worker.py（柱號對應功能將無法使用，非關鍵）" -ForegroundColor Yellow
    }
    Write-Host ""
}

# ============================================================================
# 摘要
# ============================================================================
Write-Host "============================================================================" -ForegroundColor Cyan
Write-Host "   部署摘要" -ForegroundColor Cyan
Write-Host "============================================================================" -ForegroundColor Cyan
foreach ($r in $results) {
    $colour = switch ($r.Status) { 'OK' { 'Green' } 'SKIP' { 'Yellow' } default { 'Red' } }
    Write-Host ("  Revit {0}  {1,-5} {2}" -f $r.Version, $r.Status, $r.Reason) -ForegroundColor $colour
}
Write-Host ""

$failed  = @($results | Where-Object { $_.Status -eq 'FAIL' })
$okCount = @($results | Where-Object { $_.Status -eq 'OK' }).Count

if ($failed.Count -gt 0) {
    Write-Host "[失敗] 有 $($failed.Count) 個版本部署失敗" -ForegroundColor Red
    Wait-ForExit 1
}
if ($okCount -eq 0) {
    Write-Host "[警告] 沒有任何版本被部署（全部略過）" -ForegroundColor Yellow
    Wait-ForExit 1
}

Write-Host "完成：$okCount 個版本部署成功" -ForegroundColor Green
Write-Host ""
Write-Host "接下來的步驟：" -ForegroundColor Cyan
Write-Host "  1. 完全關閉 Revit（如果正在執行）" -ForegroundColor White
Write-Host "  2. 重新開啟 Revit" -ForegroundColor White
Write-Host "  3. 應該會看到「MCP Tools」面板" -ForegroundColor White
Write-Host "  4. 點擊「MCP 服務 (開/關)」啟動服務" -ForegroundColor White
Write-Host ""
Write-Host "如有問題，請參考 README.zh-TW.md 的「常見問題」章節" -ForegroundColor Cyan
Write-Host ""

Wait-ForExit 0
