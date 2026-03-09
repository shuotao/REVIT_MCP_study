# Revit MCP 部署與對話完整紀錄 (2026-03-09)

## 1. 初始部署階段
- **操作**：從 GitHub 克隆 `REVIT_MCP_study` 專案。
- **建置**：針對 Revit 2024 使用 `RevitMCP.2024.csproj` 成功編譯 DLL。
- **安裝**：將 `RevitMCP.dll` 與 `.addin` 複製到位元於 `C:\ProgramData\Autodesk\Revit\Addins\2024` 的目錄。
- **Antigravity 配置**：修改 `mcp_config.json` 引導至 MCP Server。

## 2. 解決 Revit 插件載入問題
- **問題**：Revit 啟動後未出現插件選單。
- **原因**：`.addin` 描述檔放置路徑錯誤，且標籤格式不相容（應為 `AddInId` 而非 `ClientId`）。檔案被系統封鎖。
- **解法**：移動描述檔至根目錄、更新 XML 標籤內容，並使用 `Unblock-File` 解鎖。

## 3. 解決 Port 埠號不一致問題
- **問題**：Revit 插件介面顯示使用了 Port `8765`，但伺服器預設為 `8964`。
- **發現**：Revit 優先讀取了殘留在 `AppData\Roaming\RevitMCP\config.json` 的舊設定檔。
- **解法**：手動修改該 JSON 檔案，將 Port 修正為 `8964` 並重啟服務。

## 4. 統計與功能測試
- **目標**：統計平面圖視圖數量及房間面積。
- **技術障礙**：發現 C# 核心程式在查詢視圖 (ViewPlan) 時存在 NullReferenceException (空指標錯誤)，導致原生指令卡住。
- **備案建議**：目前建議使用專案內附的 Node.js 獨立腳本 (`list_rooms_with_area.js`) 進行資料統計。

## 5. 紀錄與歸檔
- **存檔**：將以上所有過程寫入 `log.md` 並成功 PUSH 到 Git 倉庫 `main` 分支。
