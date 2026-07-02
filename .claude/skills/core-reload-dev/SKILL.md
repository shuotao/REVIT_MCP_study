---
name: core-reload-dev
description: "進階開發者工作流（opt-in）：在 Loader/Core 熱重載分支上，不重啟 Revit 即可重建 CoreRuntime → 部署 → 重載 → 驗證，加速 CommandExecutor 類修改的迭代。觸發條件：使用者提到熱重載、core reload、不重啟 Revit、CoreRuntime、重載核心、hot reload、reload_core、開發迭代加速。"
user-invocable: true
---

Operational companion to `docs/core-reload-architecture.md` and `domain/core-reload-boundary.md`.

> [!IMPORTANT]
> **OPT-IN, NOT main 架構。** main 維持憲法的「單一 `MCP/RevitMCP.csproj`」結構，**不含** `MCP.Contracts` / `MCP.CoreRuntime` 子專案。本 skill 的步驟**只在 opt-in 熱重載分支（`feature/core-reload-optin`）上成立**。在 main（單一 csproj）執行 `dotnet build MCP.CoreRuntime/...` 會找不到專案 —— 此時請改用 `/build-revit` + `/deploy-addon`，並向使用者說明熱重載屬 opt-in 進階分支。
>
> 知識來源：收編自 ChimingLu（啟銘）的熱重載分支（issue #33 決策：核心架構 opt-in、文件與邊界收編進 main）。

## Pre-flight（先確認，否則停下）

1. 確認目前分支具備 Loader/Core 分層：存在 `MCP.CoreRuntime/MCP.CoreRuntime.csproj` 與 `MCP.Contracts/`。
   - 不存在 → 回報「此分支非熱重載分支，改用 `/build-revit` + `/deploy-addon`」並停止。
2. 確認 Revit 正在執行、且 MCP 服務曾啟動過（Loader 已載入）。
3. 確認目標版本（R22–R26）對應的 Addins 目錄存在。

## 熱重載迭代（不重啟 Revit，約 10–25 秒）

1. 改 Core 邏輯（`MCP/Core/CommandExecutor.cs` 或 `MCP/Core/Commands/CommandExecutor.*.cs`）。
2. 重建 **CoreRuntime**（非 Loader）：
   `dotnet build -c Release.R{YY} MCP.CoreRuntime/MCP.CoreRuntime.csproj --nologo`
3. 部署：覆蓋 `%APPDATA%\Autodesk\Revit\Addins\{year}\RevitMCP\runtime\RevitMCP.CoreRuntime.dll`。
   - **務必確認 `runtime\` 內沒有 `MCP.Contracts.dll`**（型別衝突來源，需刪除）。
4. 觸發重載：Revit ribbon「Core 重載」按鈕，或自動化 `node MCP-Server/scripts/core-reload-verify.js --auto --revit {year}`。
5. 驗證：比對 `create_dimension` 回傳的 `CoreVersion` before/after 是否遞增。

## 重啟邊界（改到這些要重啟 Revit，不能熱重載）

依 `domain/core-reload-boundary.md`：
- Loader：`MCP/Application.cs`、`MCP/Commands/MCPCommands.cs`
- `MCP.Contracts` 介面簽章（`IRevitMcpRuntime.cs`）
- `CoreRuntimeManager.cs` / `CoreLoadContext.cs`
- `.addin` manifest / AddInId / 載入路徑
- 只改 `MCP-Server/src/tools/*.ts` → 不需重啟 Revit，重啟 MCP Server 即可。

## 版本差異速記

| Revit | Runtime | 卸載機制 |
|---|---|---|
| 2025–2026 | .NET 8 | `AssemblyLoadContext.Unload()` + GC（真正卸載） |
| 2022–2024 | .NET FX 4.8 | `Assembly.Load(byte[])` 記憶體載入（舊 Assembly 留存，>20 次/日重載記憶體緩增，建議每日首次重新部署） |

## Error Handling

| 症狀 | 對策 |
|---|---|
| 找不到 `MCP.CoreRuntime.csproj` | 非熱重載分支 → 改用 `/build-revit` |
| 啟動錯誤「方法 'SetReloadCallback' 沒有實作」 | Loader/Contracts 已更新但 runtime DLL 是舊版 → 重建並部署 CoreRuntime；刪 `runtime\MCP.Contracts.dll` |
| 命令 8s timeout | UI 執行緒被 modal 阻塞 → 確認 `SocketService.StartAsync()` 無 modal `TaskDialog`（main 已收編此修正） |
| `CoreVersion` 重載後沒變 | `runtime\` DLL 未更新或 shadow-copy 取到舊快照 → 確認部署時間戳；清 `%TEMP%\RevitMCP\runtime-shadow\` |
| Port 8964 監聽=是但連線 timeout | HTTP.sys 孤兒 Queue → 重開機最可靠；或系統管理員 `net stop http /y` → `net start http`（勿改 port 8964） |
