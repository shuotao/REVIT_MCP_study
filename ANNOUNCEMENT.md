# 📢 專案重大更新與部署提醒 (2026-03-04)

各位開發者與參與者好，

本專案剛剛進行了一次核心代碼整合，旨在完善功能完整性並統一通訊標準。以下是本次更新的重點摘要以及您需要配合執行的部署動作：

---

## 🚀 核心更新內容

### 1. 整合 4 個全新工具定義 (MCP Tools)
為了讓 AI 能更全面地控制 Revit 並執行複雜分析，我們補齊了以下工具：
- **`unjoin_wall_joins` & `rejoin_wall_joins`**：解決牆體上色時的幾何接合干擾，確保視覺化上色流程準確無誤。
- **`get_room_daylight_info`**：正式整合居室採光分析功能（由 DAVIT 貢獻），AI 現在可以直接讀取並分析房間採光比例。
- **`get_view_templates`**：新增視圖樣版查詢工具（由 Alex Huang 貢獻），支援樣版設置、篩選器與隱藏品類的整併分析。

### 2. 通訊標準統一 (Port 修正)
- **Port 修正為 8964**：修正了部分配置文件中 port 為 8966 的不一致問題。現在全專案（C#、TypeScript、config.json）均統一使用 **8964** 作為 WebSocket 通訊埠。

### 3. 功能映射優化
- **完整對比驗證**：目前所有 TypeScript 端的工具定義與 C# 端的指令處理已完全對應（37 個工具），大幅提升了 AI 呼叫的穩定性。

---

## ⚠️ 重要：重新部署步驟與模式

由於本次更新涉及 **C# 後端邏輯** 與 **TypeScript 工具架構** 的大幅變動，請所有參與者務必重新執行以下流程：

### 第零步：Fork 專案同步 (針對 Fork 貢獻者)
如果您是透過 Fork 參與計畫，請先依照以下方式獲得最新代碼：
1. **GitHub 網頁同步**：進入您的 Fork 倉庫首頁，點擊 **「Sync fork」→「Update branch」**。
2. **本地代碼拉取**：在您的執行目錄執行 `git pull`。

### 第一步：C# Add-in 重建 (Windows)
1.  **關閉 Revit**（確保 DLL 檔案未被佔用）。
2.  開啟終端機，執行編譯：
    ```powershell
    dotnet build -c Release MCP/RevitMCP.2024.csproj
    ```
3.  執行部署腳本：
    ```powershell
    .\scripts\install-addon.ps1  # 依您的環境選擇版本
    ```

### 第二步：MCP Server 重建
1.  進入 `MCP-Server` 目錄。
2.  執行編譯以更新 JavaScript 產物：
    ```bash
    npm run build
    ```

### 第三步：重新啟動
1.  重新開啟 Revit 並啟動 MCP 連線。
2.  重新啟動您的 AI 客戶端（或手動啟動 MCP Server），確保其載入最新的工具定義。

---

**提醒**：若未完成上述重新編譯步驟，可能會出現「AI 呼叫工具失敗」或「Socket 連線埠不匹配」的錯誤。

感謝各位對 Revit MCP 專案的貢獻與支持！如有任何問題請隨時在 Issue 中提出。

---
*專案管理員 @shuotao*
