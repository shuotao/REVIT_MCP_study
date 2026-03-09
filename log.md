# Revit MCP 部署與除錯完整紀錄

**紀錄時間**：2026-03-09
**目標環境**：Revit 2024 / Node.js v24.12.0 / .NET SDK 10.0 / Windows
**專案位置**：`C:\Users\01102088\Desktop\MCP`

---

## 階段一：專案克隆與初始建置部署

### 1. 取得原始碼
- 執行 `git clone` 嘗試取得 `https://github.com/shuotao/REVIT_MCP_study.git`。
- 為了加速下載與避免卡住，最終使用 `git clone --depth 1` 成功取得原始碼並放置於桌面 MCP 資料夾。

### 2. 環境檢測與建置 C# Add-in
- 確認用戶擁有適當的 Node.js、npm 與 .NET SDK 環境。
- 進入 `MCP` 資料夾，針對 Revit 2024 版本，執行 `dotnet build -c Release.R24 RevitMCP.csproj`。
- 專案成功編譯出 `RevitMCP.dll` 位址於 `bin\Release.2024\` 內（帶有 27 個正常版本相容警告）。

### 3. 建置 Node.js MCP Server 
- 進入 `MCP-Server` 資料夾。
- 執行 `npm install` 與 `npm run build`，成功產生 `build/index.js` 作為伺服器端進入點。

### 4. 設定 Antigravity MCP 檔案
- 修改 `C:\Users\01102088\.gemini\antigravity\mcp_config.json`。
- 加入 `revit-mcp` 的配置，指向本機的 Node 執行檔與剛構建好的 `index.js`，環境變數設為 `REVIT_VERSION: 2024`。

---

## 階段二：解決 Revit 無法載入 Add-in 問題

### 1. 問題發現 
- 使用者回報啟動 Revit 後，並未出現任何 Add-in 相關的按鈕或訊息。
- 透過指令列檢視 `C:\ProgramData\Autodesk\Revit\Addins\2024` 目錄結構。

### 2. 原因分析與對策
- **目錄結構錯誤**：`.addin` 描述檔被放置在 `RevitMCP` 子資料夾內，而 Revit 只會讀取 Addins 根目錄的描述檔。
- **XML 標籤相容性**：檔案內使用了 `<ClientId>`，在某些情況未被正確識別。
- **安全限制**：從網路上下載/編譯的 DLL 與 Addin 可能被 Windows 封鎖。

### 3. 執行動作
- 將 `RevitMCP.2024.addin` 直接複製到 `Addins\2024\` 根目錄並命名為 `RevitMCP.addin`。
- 透過工具將 `.addin` 檔內的 `<ClientId>` 修改為標準的 `<AddInId>`。
- 執行 PowerShell 指令 `Unblock-File` 解除對 `.addin` 與 `RevitMCP.dll` 的封鎖。
- **結果**：成功讓 Revit 2024 識別並載入按鈕。

---

## 階段三：解決連接介面「埠號 (Port) 錯誤與斷線」問題

### 1. 問題發現 
- 使用者截圖顯示 Revit Add-in 面板上的 MCP 設定綁定了 Port `8765`。
- 利用系統測試 `ws://localhost:8964` 和 `Test-NetConnection` 確認 8964 埠號根本沒有被開啟監聽。

### 2. 原因分析與對策
- C# 原始碼 `ServiceSettings.cs` 中內建的預設 Port 是 `8964`。
- 但是 Revit 啟動時會優先讀取使用者本機快取 `C:\Users\01102088\AppData\Roaming\RevitMCP\config.json` 的設定。
- 該舊設定檔將 Port 強制設定成了 `8765`，導致了兩端建立連線的埠口（MCP-Server 用 8964，Revit 用 8765）無法對接而發生幻覺與中斷。

### 3. 執行動作
- 使用檔案編輯工具，強制修改 `C:\Users\01102088\AppData\Roaming\RevitMCP\config.json`，將其中的 `Port: 8765` 修正為 `Port: 8964`。
- 告知使用者需在 Revit 面板按下「開/關」重開一次服務，以載入新的設定檔。

---

## 階段四：測試資料交換機制與「卡住」的原因

### 1. 問題發現
- 使用者要求統計「目前在 FLOOR PLANS site plan 裡面有幾張視圖，房間有哪些，各自面積」。
- 代理(AI) 嘗試使用原生 WebSocket 指令與腳本來抓取資料，卻導致對話無回應或出現錯誤。

### 2. 測試與除錯過程
- 利用 `node -e` 撰寫臨時 WebSocket 測試腳本呼叫各種 API：
  - 測試 `CommandName: 'get_model_info'` -> 回傳 `未實作的命令` (Not Implemented)
  - 測試 `CommandName: 'query_elements', Parameters: { class: 'ViewPlan' }` -> 回傳 `Object reference not set to an instance of an object.` （空指標錯誤，C# 內部引發 Exception）。
  - 測試執行 `scripts/list_rooms_with_area.js` -> Console 顯示連線關閉，無法取得房間詳細數據，連線被直接 Dropout。

### 3. 原因分析
- 外掛（C# 端）的 `CommandExecutor.cs` 對於部分參數過濾（尤其是 Views 和 Rooms 等特定類別）存在邏輯缺陷，遇到了 NullReferenceException，導致伺服器端錯誤並直接拋棄回傳資料，甚至導致 WebSocket Socket 連線異常中斷。
- 由於此原因，AI 花費了許多指令步驟試圖透過不同參數 (`category`, `class`) 來規避這個錯誤，這也導致了畫面上看起來在「卡住」、無法給出資料的狀態。

### 4. 提出備案
- 放棄使用仍有 Bug 的某些原生指令。
- 建議使用者確保將某個內建包含房間參數的模型打開，然後請手動呼叫已配置好、功能明確的腳本 `node scripts/list_rooms_with_area.js` 或是 `node scripts/list_levels.js` 作為替代測試手段，以驗證連線狀況，並待未來對 C# Core 原始碼進行逐步除錯修復。
