# 變更日誌 (Changelog)

所有重要的專案變更都會記錄在此檔案中。

格式基於 [Keep a Changelog](https://keepachangelog.com/zh-TW/1.0.0/)。

---

## [1.5.1] - 2026-03-09

### 🐛 Bug 修正（C# — 需重新編譯）

- **`CommandExecutor.cs`**：修正 4 個問題 — null reference 風險、port 8966→8964 統一、catch 範圍過寬、重複初始化
- **`ExteriorWallOpeningChecker.cs`**：修正外牆開口條件判斷邏輯
- **`RevitMCP.csproj`**：改用 Nice3point.Revit.Sdk 6.1.0，統一支援 Revit 2022–2026

### 🆕 新增

- **`RevitCompatibility.cs`**：跨版本相容性輔助層（新增檔案）
- **`CLAUDE.md`**：Claude Code 專用上下文指引
- **`docs/MIGRATION_GUIDE.md`**：fork 成員升級遷移指南
- **`教材/`**：完整公開 8 堂課教材、投影片、延伸閱讀資源
  - 新增 `05-Skill遷移實戰篇.md`（Agent Skill 架構實作教學）

### 📁 文件整理

- **合併後刪除**：ANNOUNCEMENT.md → CHANGELOG.md、MCP_Server_Setup_Guide.md → README.md、ARCHITECTURE.md → README.md
- **搬移**：QUICK_TEST.md → docs/、4 個學習筆記 → 教材/
- **刪除**：mission.md、Domain_to_Skill_Migration_Guide.md
- **更新**：README.md 新增導覽表、README.en.md 支援 Revit 2025/2026、DOCS_STRUCTURE.md 重寫

### ⚠️ 成員需執行的動作

**C# 程式碼有更新，需重新編譯 DLL：**

```powershell
cd "專案路徑/MCP"
dotnet build -c Release.R22   # Revit 2022
dotnet build -c Release.R23   # Revit 2023
dotnet build -c Release.R24   # Revit 2024
dotnet build -c Release.R25   # Revit 2025
dotnet build -c Release.R26   # Revit 2026
```

複製 DLL → Revit Addins 資料夾 → 重啟 Revit。

**MCP Server 無變動**，不需要 `npm install` 或 `npm run build`。

---

## [1.5.0] - 2026-03-04

### ✨ 新功能

#### 整合 4 個全新 MCP 工具
- **`unjoin_wall_joins` & `rejoin_wall_joins`**：解決牆體上色時的幾何接合干擾，確保視覺化上色流程準確無誤
- **`get_room_daylight_info`**：居室採光分析功能（由 DAVIT 貢獻），AI 可直接讀取並分析房間採光比例
- **`get_view_templates`**：視圖樣版查詢工具（由 Alex Huang 貢獻），支援樣版設置、篩選器與隱藏品類的整併分析

### 🔧 改進

#### 通訊標準統一
- **Port 統一為 8964**：修正部分配置文件中 port 為 8966 的不一致問題。全專案（C#、TypeScript、config.json）均統一使用 8964

#### 功能映射優化
- **完整對比驗證**：TypeScript 端工具定義與 C# 端指令處理已完全對應（37 個工具）

### ⚠️ 重要提醒

本次更新涉及 C# 後端邏輯與 TypeScript 工具架構的大幅變動，需重新編譯部署（參考 README.md 的部署步驟）。

---

## [1.4.1] - 2024-12-18

### 🐛 Bug 修正

#### 目錄重構後的安裝腳本路徑錯誤
- **問題**：12/17 進行目錄重構（`MCP/MCP/` → `MCP/`），但安裝腳本 `install-addon-bom.ps1` 未同步更新，導致找不到 DLL
- **症狀**：
  - 執行安裝時出現「✗ 錯誤：找不到 RevitMCP.dll」
  - Revit 啟動時無法載入 MCP Plugin
- **修正**：更新所有路徑參照為單層 `MCP\` 結構
  - `MCP\bin\Release.2024\RevitMCP.dll`
  - `MCP\RevitMCP.2024.addin`
  - `MCP\bin\Release\Newtonsoft.Json.dll`
- **影響範圍**：所有透過 `install-addon-bom.ps1` 執行安裝的用戶

### 🔧 改進

- **新增 `verify-installation.ps1`**：安裝前驗證工具
  - 檢查目錄結構是否正確
  - 檢查 DLL 是否已建置
  - 檢查安裝腳本路徑是否正確
  - 提供下一步操作建議

- **更新 `GEMINI.md`**：AI 助手智能部署指南
  - 新增環境偵測協定（Revit 版本 + AI Client）
  - 提供版本特定的建置命令對照表
  - 說明 Revit 2024 的 56 個警告是正常的（API 相容性）
  - 加入常見問題處理邏輯
  - 協助 AI 助手為使用者生成客製化部署指令

### 📁 修改檔案

| 檔案 | 變更 |
|------|------|
| `scripts/install-addon-bom.ps1` | 修正 3 處路徑：DLL、.addin、Newtonsoft.Json |
| `scripts/verify-installation.ps1` | 新增安裝驗證工具 |

### ⚠️ 重要提醒

**目錄結構已統一為單層 `MCP\` 結構，所有路徑參照請確認正確：**
- ✅ 正確：`MCP\bin\Release.2024\RevitMCP.dll`
- ❌ 錯誤：`MCP\MCP\bin\Release.2024\RevitMCP.dll`

---

## [1.4.0] - 2025-12-14

### ✨ 新功能

#### 容積檢討工具
- **新增 `get_rooms_by_level`**：取得指定樓層的所有房間清單
  - 回傳：房間名稱、編號、面積
  - 回傳：中心點座標（公釐）
  - 回傳：資料完整度統計（有/無名稱的房間數量）
  - 可用於容積檢討作業

### 🐛 Bug 修正

#### 補齊 C# 實作
- **補齊 `create_floor`**：建立樓板（原本只有 TypeScript 定義，缺少 C# 實作）
- **補齊 `modify_element_parameter`**：修改元素參數
- **補齊 `create_door`**：建立門
- **補齊 `create_window`**：建立窗

### 📚 文件更新

- 更新 README.md 工具清單，補齊遺漏的工具說明
- 新增 `domain/` 資料夾，放置容積檢討開發文件

### 📁 修改檔案

| 檔案 | 變更 |
|------|------|
| `MCP-Server/src/tools/revit-tools.ts` | 新增 get_rooms_by_level 工具定義 |
| `MCP/MCP/Core/CommandExecutor.cs` | 新增 5 個命令實作 |
| `README.md` | 更新工具清單 |
| `domain/README.md` | 新增容積檢討開發計劃 |

---

## [1.3.0] - 2025-12-12

### ✨ 新功能

#### 家具工具擴展
- **新增 `get_furniture_types`**：取得專案中已載入的家具類型清單
  - 回傳：類型名稱、族群名稱、是否已啟用
  - 支援類別篩選（如：椅子、桌子、床）
- **新增 `place_furniture`**：在指定位置放置家具
  - 支援指定座標（公釐）
  - 支援指定樓層
  - 支援旋轉角度（度）
- **新增 `get_room_info`**：取得房間詳細資訊
  - 回傳：房間名稱、編號、面積
  - 回傳：中心點座標（公釐）
  - 回傳：邊界範圍 BoundingBox（MinX, MinY, MaxX, MaxY）
  - 可用於智慧放置家具

### 📚 文件更新

#### Git Pull 更新提醒
- **新增**：在 README.md 和 README.en.md 最前面加入警告區塊
- **內容**：提醒使用者 git pull 後如有 C# 程式碼變更，需重新編譯並部署 DLL
- **包含**：更新類型對照表（C#/TypeScript/設定檔各自的處理方式）

### 📁 修改檔案

| 檔案 | 變更 |
|------|------|
| `MCP-Server/src/tools/revit-tools.ts` | 新增 3 個工具定義（#14-#16） |
| `MCP/MCP/Core/CommandExecutor.cs` | 新增 3 個命令實作 + Architecture 命名空間 |
| `README.md` | 新增 Git Pull 更新提醒區塊 |
| `README.en.md` | 新增 Git Pull 更新提醒區塊（英文版） |

---

## [1.2.0] - 2025-12-11

### ✨ 新功能

#### Grid 與 Column 工具擴展
- **新增 `get_all_grids`**：取得專案中所有網格線（Grid）資訊
  - 回傳：Grid 名稱、方向（水平/垂直）、起點/終點座標（公釐）
  - 可用於計算網格交會點，配合 `create_column` 使用
- **新增 `get_column_types`**：取得專案中可用的柱類型
  - 回傳：類型名稱、族群名稱、尺寸資訊（寬x深）
  - 支援材質篩選（如：混凝土、鋼）
- **新增 `create_column`**：在指定位置建立柱子
  - 支援指定底部/頂部樓層
  - 支援指定柱類型名稱
  - 座標使用公釐，內部自動轉換為 Revit 英尺單位

### 📁 修改檔案

| 檔案 | 變更 |
|------|------|
| `MCP-Server/src/tools/revit-tools.ts` | 新增 3 個工具定義 |
| `MCP/MCP/Core/CommandExecutor.cs` | 新增 3 個命令實作 |

---

## [1.1.0] - 2025-12-11

### 🐛 Bug 修正

#### JSON 屬性名稱大小寫不匹配問題
- **問題**：MCP Server 發送的 JSON 使用 camelCase（`commandName`、`parameters`、`requestId`），但 Revit Add-in (C#) 期待 PascalCase（`CommandName`、`Parameters`、`RequestId`）
- **症狀**：透過 Gemini CLI 或其他 MCP 客戶端連線時出現「命令執行逾時」錯誤
- **修正**：修改 `MCP-Server/src/socket.ts`，將發送的 JSON 屬性名稱改為 PascalCase
- **影響範圍**：所有使用 MCP Server 的客戶端（Gemini CLI、Claude Desktop、VS Code Copilot、Antigravity）

### 📚 文件更新

#### Gemini CLI MCP 設定方式錯誤修正
- **問題**：原文件說明使用 `gemini --config gemini_mcp_config.json` 參數，但 Gemini CLI 實際上使用 `~/.gemini/settings.json` 設定 MCP
- **修正**：更新 README.md 和 README.en.md，說明正確的設定方式
- **新增內容**：
  - 設定檔位置：`%USERPROFILE%\.gemini\settings.json`
  - 如何加入 `mcpServers` 區塊
  - 使用 `/mcp list` 驗證連接

#### PowerShell 執行原則錯誤解決方法
- **問題**：Windows 預設停用 PowerShell 腳本執行，導致 `npm install` 失敗
- **新增**：在文件中加入 `Set-ExecutionPolicy` 解決方法

#### Git Clone 首次設定指南
- **問題**：透過 `git clone` 取得專案的使用者無法直接使用，因為以下檔案不包含在儲存庫中：
  - `MCP-Server/build/` - 需要執行 `npm run build` 產生
  - `MCP-Server/node_modules/` - 需要執行 `npm install` 產生
  - `MCP/MCP/bin/` - 需要編譯或下載 Release
- **新增**：在 README 中加入「⚠️ 透過 Git Clone 的首次設定」區塊

#### 設定檔硬編碼路徑問題
- **問題**：`gemini_mcp_config.json` 和 `claude_desktop_config.json` 使用硬編碼的使用者路徑
- **修正**：改為佔位符路徑 `【請修改此路徑】`，提醒使用者自行修改
- **保留**：`.vscode/mcp.json` 使用 `${workspaceFolder}` 變數，無需修改

---

## [1.0.0] - 2025-12-10

### ✨ 新功能

- 初始版本發布
- Revit Add-in (C#) 實作
- MCP Server (Node.js/TypeScript) 實作
- 支援 10 種 Revit 操作工具
- 支援多種 AI 平台（Claude Desktop、Gemini CLI、VS Code Copilot、Antigravity）

---

## 提交歷史對照

| Commit | 說明 |
|--------|------|
| `0dcdc54` | 修正 JSON 屬性名稱為 PascalCase |
| `348775d` | 修正 Gemini CLI MCP 設定說明 |
| `2e75839` | 新增 Git Clone 首次設定指南 |
| `ed7ee1b` | 新增英文版文件支援 |
| `a223ede` | 更新 WebSocket 說明與技術補充附錄 |
| `d088f06` | 新增安裝指令稿 |
