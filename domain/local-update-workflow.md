---
name: local-update-workflow
description: "本機環境同步後的更新與部署 SOP（環境專屬）：pull 後重新編譯 MCP Server、處理 Nice3point SDK 版本相容、手動部署 DLL 至 Revit Addins 目錄。當使用者提到本機更新、pull 後部署、重新編譯部署、local update、環境專屬部署流程時參考。"
metadata:
  version: "1.1"
  updated: "2026-08-11"
  created: "2026-06-15"
  references:
    - "2026-08-10 實測：Revit 2026 部署因『只複製 RevitMCP.dll』而長期缺 6 個相依，Excel 類工具必然 runtime 失敗"
  related:
    - core-reload-boundary.md
  referenced_by: []
  tags: [部署, 本機環境, local update, deploy, SDK 相容, 多版本部署, 世代防呆]
---

# Local Update Workflow (Environment Specific)

> [!NOTE]
> **環境專屬文件**：本檔記錄某一貢獻者本機環境（Revit 2025 / .NET 8 單版本）的更新流程，內含該環境專屬的絕對路徑（`c:\WIP\...`）與 `Nice3point.Revit.Sdk` 由 6.1.0 降為 6.0.0 的暫時性 workaround。此降級**非本專案的正規建置路徑**——canonical 建置與部署請依 `CLAUDE.md` 的 Build Commands 與 `scripts/install-addon.ps1` / `/deploy-addon`。保留原文供有相同 SDK 相容問題的使用者參考。

這份文件記錄了本機環境，每次從 GitHub 同步更新 (pull) 之後，AI 以及開發者應該執行的標準更新與部署流程。請未來的 AI 執行更新相關任務時，嚴格遵守以下路徑與步驟。

## 環境背景與限制

> 以下是**原始撰寫者的環境快照**，用來說明本 SOP 誕生的脈絡，不是限制。
> 本流程適用 Revit 2022–2026，把下方指令中的版本／組態換成你自己的即可（對應表見步驟 3）。

- **使用者系統**：Windows
- **Revit 目標版本**：2025 (`Release.R25`)　←　原始撰寫環境，非唯一支援版本
- **.NET SDK 環境**：本機目前只有安裝 `.NET SDK 8.0.x`。
- **已知問題**：如果上游更新時將 `Nice3point.Revit.Sdk` 更新至 `6.1.0` 或以上，在使用 `dotnet build` 時會因為載入 MSBuild 任務失敗（報錯 `System.Runtime, Version=10.0.0.0`）而中斷。
- **核心路徑**：`<REPO_ROOT>`（原始貢獻者環境為 `c:\WIP\REVIT_MCP`）

---

## 每次同步/更新後的標準操作步驟

如果使用者要求「更新」、「部署」或「重新編譯整個專案」，請照順序執行以下三個步驟：

### 1. 重新編譯 MCP Server
MCP Server 是 Node.js 專案，需要重新安裝套件與編譯 TypeScript。
- **路徑**：`<REPO_ROOT>\MCP-Server`
- **指令**（請在 PowerShell 依序執行，或分開下達指令避免 `&&` 語法相容問題）：
  ```powershell
  npm install
  npm run build
  ```

### 2. 解決 C# 專案版本衝突與重新編譯
- **路徑**：`<REPO_ROOT>\MCP\RevitMCP.csproj`
- **降級 Sdk（如果需要）**：
  在執行編譯前，先檢驗 `RevitMCP.csproj` 檔案第一行的 `Sdk` 屬性。如果是 `Nice3point.Revit.Sdk/6.1.0`，必須將它改回 `6.0.0`：
  ```xml
  <Project Sdk="Nice3point.Revit.Sdk/6.0.0">
  ```
  這樣才能相容本機的 .NET 8.0 SDK。
- **執行編譯指令**：
  ```powershell
  # 工作目錄需在 <REPO_ROOT>\MCP
  # 把 R25 換成你的目標版本：2022→R22、2023→R23、2024→R24、2025→R25、2026→R26
  dotnet build -c Release.R25 RevitMCP.csproj
  ```

### 3. 部署到對應的 Revit 版本

`.csproj` 的 `<DeployAddin>` 預設為 `false`，編譯後不會自動部署。**一律使用安裝腳本，不要手動 `Copy-Item`。**

```powershell
# 部署到指定版本（把 2024 換成你的版本）
.\scripts\install-addon.ps1 -Version 2024

# CI／自動化：非互動模式，以 exit code 表達結果
.\scripts\install-addon.ps1 -Version 2024 -NonInteractive

# 多版本環境一次部署完
.\scripts\install-addon.ps1 -All -NonInteractive
```

腳本會複製**建構產物中的全部 DLL**、部署後逐檔 SHA256 驗證、備份現行版本並輪替（預設保留 3 份）。

> ⚠️ **不要手動只複製 `RevitMCP.dll`。** 這份文件先前寫著「只有 `RevitMCP.dll` 需要被覆蓋與複製」——那是錯的，
> 而且造成過實際損害：Revit 2026 的部署因此長期缺少 `ClosedXML`、`DocumentFormat.OpenXml`、`ExcelNumberFormat`、
> `Irony`、`SixLabors.Fonts`、`XLParser` 六個相依，所有 Excel 相關工具在該版本必然 runtime 拋 `FileNotFoundException`，
> 直到 2026-08-10 才被發現。
>
> 相依數量**依世代不同**，不要硬記：`Release.R22`/`R23`/`R24` 為 **13 個**（.NET Framework 4.8，含 5 個相容 shim）、
> `Release.R25`/`R26` 為 **8 個**（.NET 8 由 runtime 提供）。兩者都正確 —— 所以正確作法是「複製整個建構產物目錄」，
> 而不是維護一份會過期的檔案清單。

> ⚠️ **不要指望「跑一下腳本它自己會挑版本」。** 舊版腳本在偵測到多個 Revit 時會靜默選最高版本，
> 可能把建構丟到你沒有意圖的 Revit。現行版本要求明確 `-Version` 或 `-All`；非互動模式下未指定會直接失敗而非猜測。
> `MCP/Core/RevitCompatibility.cs` 的 `IdType` 在 `Int32`／`Int64` 間依 `REVIT2025_OR_GREATER` 分歧，
> 錯代部署**在複製階段完全沒有徵兆**，只會在 Revit 載入或呼叫時才爆 —— 腳本的世代防呆就是為此存在。
