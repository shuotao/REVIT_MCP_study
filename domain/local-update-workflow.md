---
name: local-update-workflow
description: "本機環境同步後的更新與部署 SOP（環境專屬）：pull 後重新編譯 MCP Server、處理 Nice3point SDK 版本相容、手動部署 DLL 至 Revit Addins 目錄。當使用者提到本機更新、pull 後部署、重新編譯部署、local update、環境專屬部署流程時參考。"
metadata:
  version: "1.0"
  updated: "2026-06-15"
  created: "2026-06-15"
  references: []
  related:
    - core-reload-boundary.md
  referenced_by: []
  tags: [部署, 本機環境, local update, deploy, SDK 相容, Revit 2025]
---

# Local Update Workflow (Environment Specific)

> [!NOTE]
> **環境專屬文件**：本檔記錄某一貢獻者本機環境（Revit 2025 / .NET 8 單版本）的更新流程，內含該環境專屬的絕對路徑（`c:\WIP\...`）與 `Nice3point.Revit.Sdk` 由 6.1.0 降為 6.0.0 的暫時性 workaround。此降級**非本專案的正規建置路徑**——canonical 建置與部署請依 `CLAUDE.md` 的 Build Commands 與 `scripts/install-addon.ps1` / `/deploy-addon`。保留原文供有相同 SDK 相容問題的使用者參考。

這份文件記錄了本機環境，每次從 GitHub 同步更新 (pull) 之後，AI 以及開發者應該執行的標準更新與部署流程。請未來的 AI 執行更新相關任務時，嚴格遵守以下路徑與步驟。

## 環境背景與限制

- **使用者系統**：Windows
- **Revit 目標版本**：2025 (`Release.R25`)
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
  dotnet build -c Release.R25 RevitMCP.csproj
  ```

### 3. 將編譯好的 DLL 部署到 Revit 2025 目錄
由於本專案在 `.csproj` 中預設將 `<DeployAddin>` 設為 `false`，編譯後並不會自動部署，需經由以下指令手動複製：
- **操作目錄**：`<REPO_ROOT>\MCP`
- **PowerShell 指令**：
  ```powershell
  mkdir "$env:APPDATA\Autodesk\Revit\Addins\2025\RevitMCP" -Force
  Copy-Item ".\bin\Release.R25\RevitMCP.dll" "$env:APPDATA\Autodesk\Revit\Addins\2025\RevitMCP\" -Force
  ```
> **注意**：只有 `RevitMCP.dll` 需要被覆蓋與複製，而 `.addin` 檔應該已經存在於正確的位置，不要去覆寫或產生出錯版本的 manifest 檔案。
