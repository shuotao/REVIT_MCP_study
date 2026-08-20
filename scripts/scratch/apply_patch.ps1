# 1. Revert changes to start fresh
git checkout C:\Users\User\Desktop\REVIT_MCP_study\scripts\setup.ps1

# 2. Read file as string
$filePath = "C:\Users\User\Desktop\REVIT_MCP_study\scripts\setup.ps1"
$content = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)

# Normalize CRLF to LF
$content = $content -replace "`r`n", "`n"

# 3. Replace Invoke-ExternalCommand
$target1 = @'
function Invoke-ExternalCommand {
    # Runs a native-command scriptblock (that redirects stderr with 2>&1) without
    # letting $ErrorActionPreference = "Stop" turn ordinary stderr output (npm/dotnet
    # progress or warning lines) into a terminating exception under PowerShell 5.1.
    # $LASTEXITCODE from the native call remains readable by the caller afterwards.
    param([Parameter(Mandatory)][scriptblock]$Command)
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command
    }
    finally {
        $ErrorActionPreference = $prevEAP
    }
}
'@ -replace "`r`n", "`n"

$replace1 = @'
function Invoke-ExternalCommand {
    # Runs a native-command scriptblock (that redirects stderr with 2>&1) without
    # letting $ErrorActionPreference = "Stop" turn ordinary stderr output (npm/dotnet
    # progress or warning lines) into a terminating exception under PowerShell 5.1.
    # $LASTEXITCODE from the native call remains readable by the caller afterwards.
    param([Parameter(Mandatory)][scriptblock]$Command)
    $prevEAP = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        # Temporarily turn off strict mode so strict mode settings do not bleed into external PS wrapper scripts like npm.ps1
        Set-StrictMode -Off
        & $Command
    }
    finally {
        # Restore strict mode to Latest
        Set-StrictMode -Version Latest
        $ErrorActionPreference = $prevEAP
    }
}
'@ -replace "`r`n", "`n"

if ($content.Contains($target1)) {
    $content = $content.Replace($target1, $replace1)
    Write-Host "Success: Patch 1 (Invoke-ExternalCommand) applied."
} else {
    Write-Warning "Failed: Patch 1 target not found."
}

# 4. Replace Revit process check (single line replacement)
$target2 = '$revitProcess = Get-Process -Name "Revit" -ErrorAction SilentlyContinue'
$replace2 = '$revitProcess = Get-Process | Where-Object { $_.ProcessName -eq "Revit" }'

if ($content.Contains($target2)) {
    $content = $content.Replace($target2, $replace2)
    Write-Host "Success: Patch 2 (Revit Process Check) applied."
} else {
    Write-Warning "Failed: Patch 2 target not found."
}

# 5. Replace .NET SDK Check using literal substring replacement
$startMarker = "    # --- .NET SDK ---"
$endMarker = "# ============================================================================"

$startIdx = $content.IndexOf($startMarker)
if ($startIdx -ge 0) {
    $endIdx = $content.IndexOf($endMarker, $startIdx)
    if ($endIdx -ge 0) {
        $oldBlock = $content.Substring($startIdx, $endIdx - $startIdx)
        
        $newNetSDK = @'
    # --- .NET SDK ---
    $hasDotnet = Test-CommandAvailable "dotnet"
    $hasNet8 = $false
    if ($hasDotnet) {
        try {
            $sdkList = (dotnet --list-sdks 2>$null)
            if ($sdkList) {
                foreach ($line in $sdkList) {
                    if ($line -match "^([8-9]|\d{2,})\.") { $hasNet8 = $true }
                }
            }
        }
        catch {}
    }

    if ($hasNet8) {
        Write-OK ".NET 8 SDK 已安裝（符合編譯需求）"
        Add-Result ".NET SDK" "OK" ".NET 8+ available"
    }
    else {
        Write-Fail "未偵測到 .NET 8 SDK（編譯 Revit Add-in 需要此元件）"
        if ($hasWinget) {
            Write-Info "正在透過 winget 安裝 .NET 8 SDK..."
            try {
                $wingetOutput = Invoke-ExternalCommand { & winget install Microsoft.DotNet.SDK.8 --scope user --accept-source-agreements --accept-package-agreements 2>&1 }
                Refresh-PathEnv
                $sdkList = (dotnet --list-sdks 2>$null)
                $hasNet8After = $false
                if ($sdkList) {
                    foreach ($line in $sdkList) {
                        if ($line -match "^([8-9]|\d{2,})\.") { $hasNet8After = $true }
                    }
                }
                if ($hasNet8After) {
                    Write-OK ".NET 8 SDK 安裝完成"
                    Add-Result ".NET SDK" "OK" "Installed .NET 8"
                }
                else {
                    Write-Info ".NET 8 SDK 安裝完成，但可能需要重新載入環境變數或重啟電腦"
                    Write-Info "請關閉此視窗，重新開啟後再執行一次 setup.bat"
                    Add-Result ".NET SDK" "WARN" "Installed, needs restart"
                }
            }
            catch {
                Write-Fail "winget 安裝失敗：$($_.Exception.Message)"
                Write-Info "請手動前往 https://dotnet.microsoft.com/download 下載 .NET 8 SDK"
                Add-Result ".NET SDK" "FAIL" "Install failed"
            }
        }
        else {
            Write-Host ""
            Write-Host "    請依照以下步驟手動安裝 .NET 8 SDK：" -ForegroundColor White
            Write-Host "    1. 開啟瀏覽器，前往 https://dotnet.microsoft.com/download" -ForegroundColor White
            Write-Host "    2. 下載並安裝 .NET 8 SDK" -ForegroundColor White
            Write-Host "    3. 安裝完成後，關閉此視窗重新執行 setup.bat" -ForegroundColor White
            Write-Host ""
            Add-Result ".NET SDK" "FAIL" "Not installed, no winget"
            
            if (-not $NonInteractive) {
                Read-Host "  安裝 .NET SDK 後按 Enter 重試，或直接按 Enter 繼續（後續步驟可能失敗）"
                Refresh-PathEnv
                $sdkList = (dotnet --list-sdks 2>$null)
                $hasNet8After = $false
                if ($sdkList) {
                    foreach ($line in $sdkList) {
                        if ($line -match "^([8-9]|\d{2,})\.") { $hasNet8After = $true }
                    }
                }
                if ($hasNet8After) {
                    Write-OK "偵測到 .NET 8 SDK"
                    $script:results = $script:results | Where-Object { $_.Name -ne ".NET SDK" }
                    Add-Result ".NET SDK" "OK" "v8.0+"
                }
            }
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
'@ -replace "`r`n", "`n"

        $content = $content.Replace($oldBlock, $newNetSDK + "`n`n")
        Write-Host "Success: Patch 3 (.NET SDK Check) applied."
    } else {
        Write-Warning "Failed: Patch 3 end marker not found."
    }
} else {
    Write-Warning "Failed: Patch 3 start marker not found."
}

# 6. Replace Port Check 1, 2, 3
$target4 = '($listeners | Where-Object { $_.Port -eq 8964 }).Count'
$replace4 = '@($listeners | Where-Object { $_.Port -eq 8964 }).Count'

$target5 = '($listeners2 | Where-Object { $_.Port -eq 8964 }).Count'
$replace5 = '@($listeners2 | Where-Object { $_.Port -eq 8964 }).Count'

$target6 = '($listeners3 | Where-Object { $_.Port -eq 8964 }).Count'
$replace6 = '@($listeners3 | Where-Object { $_.Port -eq 8964 }).Count'

if ($content.Contains($target4)) {
    $content = $content.Replace($target4, $replace4)
    Write-Host "Success: Patch 4 applied."
} else {
    Write-Warning "Failed: Patch 4 target not found."
}

if ($content.Contains($target5)) {
    $content = $content.Replace($target5, $replace5)
    Write-Host "Success: Patch 5 applied."
} else {
    Write-Warning "Failed: Patch 5 target not found."
}

if ($content.Contains($target6)) {
    $content = $content.Replace($target6, $replace6)
    Write-Host "Success: Patch 6 applied."
} else {
    Write-Warning "Failed: Patch 6 target not found."
}

# 7. Replace npm with npm.cmd
$content = $content.Replace('& npm install 2>&1', '& npm.cmd install 2>&1')
$content = $content.Replace('& npm run build 2>&1', '& npm.cmd run build 2>&1')
Write-Host "Success: Patch 7 (npm bypass) applied."

# Convert LF back to CRLF before writing
$content = $content -replace "`n", "`r`n"

# Write back to file with UTF-8 with BOM
$utf8BOM = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($filePath, $content, $utf8BOM)
Write-Host "Patch complete."
