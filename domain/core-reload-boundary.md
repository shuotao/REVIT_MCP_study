# Core Reload 邊界與效益評估

## 目的

整理 Revit MCP 在 Loader/Core 架構下的「可熱重載範圍」與「仍需重啟條件」，作為團隊開發與維運的共用知識。

---

## 技術架構

### 分層模型

1. Loader 層（`RevitMCP.dll`）
   - 職責：Revit Add-in 入口、Ribbon 命令註冊、Core 生命週期管理。
   - 代表檔案：`MCP/Application.cs`、`MCP/Commands/MCPCommands.cs`。
2. Contracts 層（`MCP.Contracts.dll`）
   - 職責：定義 Loader/Core 共享介面（例如 `IRevitMcpRuntime`）。
   - 特性：必須由單一 load context 提供，避免型別同名不同實例。
3. Core Runtime 層（`RevitMCP.CoreRuntime.dll`）
   - 職責：實際 Revit 命令執行、WebSocket 指令處理。
   - 代表檔案：`MCP/Core/CommandExecutor.cs` 與 `MCP/Core/Commands/CommandExecutor.*.cs`。
4. MCP Server 層（Node.js）
   - 職責：註冊 tools、把工具請求轉成 Revit command。
   - 代表檔案：`MCP-Server/src/tools/*.ts`、`MCP-Server/src/socket.ts`。

### 載入流程（Core reload）

1. Revit 啟動時載入 Loader（`RevitMCP.dll`）。
2. Loader 透過 `CoreRuntimeManager` 載入 Core runtime。
3. 使用者觸發 `Core 重載` 後，Loader 卸載舊 Core，再載入新 Core。
4. 為避免 DLL 鎖定，Loader 先將 `runtime` 內容 shadow-copy 到暫存目錄，再由暫存路徑載入。

---

## 本次修改內容

### 核心機制

1. `MCP/Core/CoreRuntimeManager.cs`
   - 新增 shadow-copy 載入流程，解除執行中 DLL 鎖檔問題。
   - 補上 unload 後的暫存目錄清理（best-effort）。
2. `MCP/Core/CoreLoadContext.cs`
   - 明確排除 `MCP.Contracts` 由自訂 context 載入，避免 Loader/Core 之間發生型別轉型衝突。
3. `MCP/RevitMCP.csproj`
   - 調整 runtime artifact 複製策略，確保 `runtime` 目錄不帶 `MCP.Contracts.dll`。

### Loader 與操作面

1. `MCP/Commands/MCPCommands.cs`
   - 保留 `Core 重載` 操作入口與狀態診斷改善。
2. `scripts/install-addon.ps1`
   - 修正版本組態與部署目錄對應。
   - 確保主 DLL、Contracts、runtime DLL 複製到 `.addin` 實際指向路徑。

### 邊界原則（變更歸屬）

1. Core 命令邏輯改動：可透過 Core reload 生效。
2. Loader/Manifest/Contracts 改動：仍屬重啟邊界。

---

## 已驗證結論（本次討論串）

1. `RevitMCP.CoreRuntime.dll` 可在不重啟 Revit 的前提下更新。
2. 觸發方式是 Revit Ribbon 的 `Core 重載`，由 Loader 重新載入 Core runtime。
3. Loader 端加入 shadow-copy 後，執行中的 runtime DLL 不再鎖住來源檔，可覆蓋新 DLL 再重載。
4. `CommandExecutor.cs` 屬於 Core runtime 編譯範圍，理論上與實測都可走不重啟流程。

---

## 可不重啟 Revit 的變更範圍

以下類型可走「編譯 CoreRuntime -> 覆蓋 runtime DLL -> Core 重載」：

1. `MCP/Core/CommandExecutor.cs` 內部命令邏輯。
2. `MCP/Core/Commands/CommandExecutor.*.cs` 的 partial command。
3. 其他被 `MCP.CoreRuntime.csproj` 連結編譯進 `RevitMCP.CoreRuntime.dll` 的檔案。

重點：以「是否進入 Core runtime assembly」判斷，而不是只看檔案放在哪個資料夾。

---

## 仍需重啟 Revit 的情況

以下變更至少需要重啟一次 Revit（有些情況建議完整重啟）：

1. Loader 層變更：
   - `MCP/Application.cs`
   - `MCP/Commands/MCPCommands.cs`
   - 其他編進 `RevitMCP.dll`（Loader）而非 Core runtime 的邏輯
2. `.addin` manifest / AddInId / 載入路徑變更。
3. `MCP.Contracts` 介面簽章變更（避免 Loader/Core 型別契約不一致）。
4. AssemblyLoadContext 或載入策略本身變更（通常在 Loader 側）。

---

## 不需重啟 Revit、但可能需重啟 MCP Server 的情況

1. `MCP-Server/src/tools/*.ts` 新增或修改 tool 定義。
2. `MCP-Server` 的工具描述、schema、profile 載入組態。

說明：這類變更主要影響 Node 端工具註冊，通常重啟 MCP Server 即可，不影響 Revit 進程。

---

## 建議開發流程（Core 命令）

1. 修改 Core 命令（例如 `CommandExecutor.cs`）。
2. `dotnet build MCP.CoreRuntime/MCP.CoreRuntime.csproj -c Release.R26`。
3. 覆蓋 `%APPDATA%/Autodesk/Revit/Addins/2026/RevitMCP/runtime/RevitMCP.CoreRuntime.dll`。
4. 在 Revit 按一次 `Core 重載`。
5. 用最小命令（例如 `get_project_info` 或目標命令）做 smoke test。

---

## 效率估算

### 估算假設

1. 一次「完整重啟 Revit + 回到可測狀態」約 90-180 秒。
2. 一次「Core 編譯 + 覆蓋 DLL + Core 重載」約 10-25 秒。

### 單次迭代節省

令：

$$
\Delta t = t_{restart} - t_{core\_reload}
$$

取中位數估計：

$$
\Delta t = 120s - 15s = 105s
$$

每次迭代約節省 1.75 分鐘，約 $87.5\%$ 等待時間。

### 每日節省（範例）

1. 10 次迭代/日：節省約 17.5 分鐘。
2. 20 次迭代/日：節省約 35 分鐘。
3. 30 次迭代/日：節省約 52.5 分鐘。

### 保守區間

1. 最保守：$90s - 25s = 65s$（每次仍省 1.08 分鐘）。
2. 最樂觀：$180s - 10s = 170s$（每次可省 2.83 分鐘）。

---

## 風險與備註

1. 若 runtime 與 contracts 版本錯配，可能出現型別轉型失敗。
2. 若部署路徑與 `.addin` 指向不一致，會誤判成「重載失效」。
3. 效率數字為估算值，建議後續以實測（每次迭代時間）持續校正。
