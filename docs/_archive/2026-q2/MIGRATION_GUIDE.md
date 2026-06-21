# 🔄 統一建構遷移指南 (Unified Build Migration Guide)

> **適用對象**：已部署舊版 Revit MCP 的使用者（使用 `RevitMCP.2024.csproj` 或舊建構命令）  
> **遷移日期**：2026-03-09

---

## 📋 變更摘要

| 項目 | 舊版 | 新版 |
|-----|------|------|
| **建構系統** | 多個 .csproj（RevitMCP.csproj + RevitMCP.2024.csproj） | 統一 `RevitMCP.csproj`（Nice3point.Revit.Sdk） |
| **支援版本** | Revit 2022–2024 | Revit 2022–2026 |
| **建構命令** | `dotnet build -c Release` | `dotnet build -c Release.RXX`（XX = 版本號） |
| **ElementId** | 僅支援 int（2022-2024） | 自動適配 int/long（透過 RevitCompatibility.cs） |
| **.NET** | 僅 .NET Framework 4.8 | 4.8（Revit 2022–2024）/ .NET 8（2025–2026） |

---

## 🚀 遷移步驟

### 第一步：拉取最新程式碼

```powershell
cd "您的專案路徑"
git pull origin main
```

### 第二步：還原 NuGet 套件

新的統一 .csproj 使用 Nice3point.Revit.Sdk，首次需要還原套件：

```powershell
cd MCP
dotnet restore RevitMCP.csproj
```

### 第三步：使用新命令編譯

**舊命令 → 新命令對照表**：

| Revit 版本 | 舊命令 | 新命令 |
|-----------|--------|--------|
| 2022 | `dotnet build -c Release RevitMCP.csproj` | `dotnet build -c Release.R22 RevitMCP.csproj` |
| 2023 | `dotnet build -c Release RevitMCP.csproj` | `dotnet build -c Release.R23 RevitMCP.csproj` |
| 2024 | `dotnet build -c Release RevitMCP.2024.csproj` | `dotnet build -c Release.R24 RevitMCP.csproj` |
| 2025 | (不支援) | `dotnet build -c Release.R25 RevitMCP.csproj` |
| 2026 | (不支援) | `dotnet build -c Release.R26 RevitMCP.csproj` |

### 第四步：部署 DLL

```powershell
# 使用自動安裝腳本（推薦）
.\scripts\install-addon.ps1

# 或手動複製（將 2024 改為您的版本）
Copy-Item "MCP\bin\Release\RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\2024\RevitMCP\" -Force
```

> ⚠️ **注意**：新版統一輸出到 `bin\Release\`，不再使用 `bin\Release.2024\`。

### 第五步：重建 MCP Server

```powershell
cd MCP-Server
npm install
npm run build
```

### 第六步：重新啟動

1. 關閉 Revit → 重新開啟
2. 重新啟動 AI 客戶端

---

## ❓ 常見問題

### Q: 舊的 `RevitMCP.2024.csproj` 還能用嗎？

檔案仍保留在倉庫中作為參考，但**不再維護**。建議遷移到統一的 `RevitMCP.csproj`。

### Q: `dotnet build -c Release.R24` 報錯 "Invalid configuration"？

確認已執行 `git pull` 取得最新的 `RevitMCP.csproj`，且已執行 `dotnet restore` 還原 Nice3point.Revit.Sdk 套件。

### Q: Revit 2025/2026 的 ElementId 變更會影響現有功能嗎？

不會。`RevitCompatibility.cs` 提供了 `GetIdValue()` 和 `ToElementId()` 擴充方法，透過條件編譯自動適配：
- Revit 2022–2024：使用 `int`（`.IntegerValue`）
- Revit 2025–2026：使用 `long`（`.Value`）

### Q: 需要修改 .addin 檔案嗎？

不需要。`RevitMCP.addin` 使用相對路徑 `RevitMCP.dll`，無需修改。

---

## 🔧 新增檔案說明

| 新檔案 | 說明 |
|--------|------|
| `MCP/Core/RevitCompatibility.cs` | Revit API 跨版本相容層（ElementId int↔long） |

## 📝 已修改檔案清單

| 檔案 | 變更內容 |
|------|---------|
| `MCP/RevitMCP.csproj` | 改用 Nice3point.Revit.Sdk，支援 R22–R26 多組態 |
| `MCP/Core/CommandExecutor.cs` | 所有 ElementId 操作改用 `GetIdValue()` |
| `MCP/Core/ExteriorWallOpeningChecker.cs` | 同上 |
| `CLAUDE.md` | 更新建構矩陣與指令 |
| `GEMINI.md` | 更新版本對照表與 AI 行為準則 |
| `README.md` | 更新版本支援範圍、建構指令 |
| `ANNOUNCEMENT.md` | 更新建構指令 |
| `教材/03-安裝篇.md` | 更新安裝步驟 |
| `scripts/install-addon.ps1` | 支援 Revit 2025/2026 偵測 |
| `scripts/install-addon-bom.ps1` | 同上 |
| `scripts/verify-installation.ps1` | 更新檢測邏輯 |
