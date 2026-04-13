# CLAUDE.md 記載的 Build 輸出路徑錯誤，導致部署舊版 DLL

## 問題描述

CLAUDE.md 中記載的編譯輸出路徑為 `bin\Release\RevitMCP.dll`，但實際使用 `dotnet build -c Release.R{YY}` 編譯後，輸出路徑為 `bin\Release.R{YY}\RevitMCP.dll`。AI 助手依照文件指示部署時，會從 `bin\Release\` 取得舊版 DLL，造成功能缺失（如 Ribbon 按鈕消失）且難以察覺。

## 根本原因

CLAUDE.md 的 Deployment Rules 區段有兩處路徑記載錯誤：

### 錯誤 1：Build 輸出路徑描述不正確

```markdown
All output to `bin\Release\RevitMCP.dll`. Each build overwrites the previous.
```

實際行為：`dotnet build -c Release.R24` 輸出至 `bin\Release.R24\RevitMCP.dll`，而非 `bin\Release\`。

### 錯誤 2：部署指令使用錯誤的來源路徑

```powershell
Copy-Item "bin/Release/RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\{version}\RevitMCP\" -Force
```

應為：

```powershell
Copy-Item "bin/Release.R{YY}/RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\{version}\RevitMCP\" -Force
```

## 修復內容

| 檔案 | 區段 | 修改 |
|------|------|------|
| `CLAUDE.md` | Deployment Rules → Multi-Version Build | `All output to bin\Release\RevitMCP.dll` → `Output to bin\Release.R{YY}\RevitMCP.dll` |
| `CLAUDE.md` | Build Commands → After building | `bin/Release/RevitMCP.dll` → `bin/Release.R{YY}/RevitMCP.dll` |

## 影響範圍

- 所有依據 CLAUDE.md 執行部署的 AI 助手（Claude Code、Gemini CLI、VS Code Copilot）都會受影響
- `bin\Release\` 中若殘留早期編譯的舊版 DLL，部署後 Revit 會載入舊版，出現功能缺失但不會報錯，極難排查
- 不影響使用 `/build-revit` 和 `/deploy-addon` Skills 的流程（Skills 內部可能有獨立的路徑邏輯）

## 重現步驟

1. 按照 CLAUDE.md 執行 `dotnet build -c Release.R24 RevitMCP.csproj`
2. 按照 CLAUDE.md 執行 `Copy-Item "bin/Release/RevitMCP.dll" ...`
3. 啟動 Revit → Ribbon 面板只顯示 2 個按鈕（缺少「開啟日誌」）
4. 檢查發現 `bin\Release\` 中的 DLL 是舊版本，新版在 `bin\Release.R24\`

## 請求

1. 修正 CLAUDE.md 中所有涉及 `bin\Release\` 的路徑為 `bin\Release.R{YY}\`
2. 考慮清除 `bin\Release\` 中的殘留舊 DLL，避免未來再次誤用
3. 確認 `/build-revit` 和 `/deploy-addon` Skills 中的路徑是否一致

## 原始貢獻者：__@lesleyliuke__
