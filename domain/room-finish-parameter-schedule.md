---
name: room-finish-parameter-schedule
description: "房間粉刷專案與共用參數批次綁定、與房間粉刷計畫明細表自動生成 SOP。涵蓋 MCP Tool 與 pyRevit 雙軌執行邏輯、共享參數綁定 Room 品類、明細表 6 大欄位自動排序與獨立建立。"
metadata:
  version: "1.0"
  updated: "2026-07-29"
  created: "2026-07-29"
  contributors:
    - "CyberPotato0416"
  references: []
  related:
    - finish-schedule-governance.md
    - room-surface-area-review.md
    - finish-legend-creation.md
  tags: [room-finish, shared-parameters, view-schedule, mcp-tool, pyrevit, schedule-automation]
---

# 房間粉刷專案/共用參數批次寫入與明細表生成 SOP

## 1. 目的與業務背景

在 BIM 專案執行過程中，房間粉刷屬性（如樓層粉刷代號、牆面粉刷代號、天花板粉刷代號）若外部有明細表，可以匯入至 Revit 房間模型中以供後續清冊產出與圖面標註使用。

傳統人工操作存在以下痛點：
1. **參數設定繁瑣**：必須在 Revit 手動載入共享參數檔（Shared Parameter File）並將參數個別綁定至房間 (`OST_Rooms`) 品類。
2. **明細表建立耗時**：手動建立明細表時，需在幾百個欄位中手動挑選並調整欄位前後順序。

本 SOP 規範如何透過 **MCP (Model Context Protocol) 服務** 與 **pyRevit 自動化工具** 雙軌實現「獨立批次綁定房間共用參數」與「獨立排版建立明細表」。

---

## 2. 獨立模組與執行解耦 (Standalone Capability)

本 SOP 之功能具備 **完全解耦與獨立執行** 特性，使用者與 Agent 可根據執行需求選擇獨立執行或連續串接：

| 獨立模組名稱 | 可否獨立執行 | 執行目的與獨立運作條件 |
| :--- | :--- | :--- |
| **模組 A：共用參數批次寫入** (`BatchAddRoomParams`) | **可獨立執行** | 僅於 Revit 專案中將 `樓層粉刷代號`、`牆面粉刷代號`、`天花板粉刷代號` 綁定至 Room 品類。不依賴明細表建立。 |
| **模組 B：明細表一鍵生成** (`CreateJJPRoomSchedule`) | **可獨立執行** | 僅於 Revit 中自動建立 `房間粉刷計畫明細表(從業主CAD提取)` 並排版 6 大欄位。若專案中尚無共用參數，會建立表單結構並標示未找到欄位。 |

---

## 3. 輸入參數與目標欄位

### 目標參數清單
| 參數名稱 | 資料類型 | 綁定品類 | 說明 |
| :--- | :--- | :--- | :--- |
| `樓層粉刷代號` | 文字 (Text) | 房間 (`OST_Rooms`) | 存放地坪粉刷代碼 (F) |
| `牆面粉刷代號` | 文字 (Text) | 房間 (`OST_Rooms`) | 存放牆面粉刷代碼 (W) |
| `天花板粉刷代號` | 文字 (Text) | 房間 (`OST_Rooms`) | 存放天花板粉刷代碼 (C) |

### 明細表產出規範
* **明細表預設名稱**：`房間粉刷計畫明細表(從業主CAD提取)`
* **欄位標準排序**：
  1. 房間名稱 (`Name`)
  2. 房間編號 (`Number`)
  3. `樓層粉刷代號`
  4. `牆面粉刷代號`
  5. `天花板粉刷代號`
  6. 樓層 (`Level`)

---

## 4. Agent 與 MCP 工具執行 SOP (MCP Workflow)

當 AI Agent 透過 MCP Protocol 進行自動化操作時，執行流程如下：

### 步驟 1：檢測並批次綁定專案/共用參數（模組 A）
1. Agent 呼叫 MCP 查詢工具，確認模型中的 `OST_Rooms` 品類是否已包含 `樓層粉刷代號`、`牆面粉刷代號`、`天花板粉刷代號` 參數。
2. 若參數不存在，Agent 透過 Revit API / MCP Parameter Binding 接口：
   - 讀取共享參數檔 (`.txt`) 定義。
   - 建立 `InstanceBinding` 並將上述參數綁定至 `BuiltInCategory.OST_Rooms` 品類，歸類於 `Data` / `Text` 群組。

### 步驟 2：自動創建並設置明細表視圖（模組 B）
1. Agent 呼叫 MCP `create_view_schedule`（或對應 ViewSchedule API）：
   - 品類指定為 `OST_Rooms` (房間)。
   - 視圖名稱設為 `房間粉刷計畫明細表(從業主CAD提取)`（若已存在同名明細表，自動後綴 `_1`, `_2` 避免衝突）。
2. Agent 對該 ViewSchedule 之 Definition 進行欄位編排：
   - 呼叫 `GetSchedulableFields()` 檢索可排入的欄位。
   - 嚴格依照 [`房間名稱`, `房間編號`, `樓層粉刷代號`, `牆面粉刷代號`, `天花板粉刷代號`, `樓層`] 之順序 `AddField()`。
3. 將新建明細表設為當前 Active View 並回報執行結果。

---

## 4. 驗證與異常處理機制

1. **共享參數檔遺失**：若 Revit 當前未綁定 `.txt` 共享參數檔，提示使用者手動指定路徑後繼續執行。
2. **重複欄位防呆**：寫入參數前先檢查 `ParameterBindings`，若參數已存在則自動跳過，確保冪等性 (Idempotency)。
3. **明細表重名防呆**：自動檢索現有 ViewSchedule 名稱，遇重名時自動遞增編號。

---

## 5. pyRevit 實體腳本對照

本 Domain 規範對應之實體 pyRevit 按鈕腳本 (`.py` & `bundle.yaml`) 存放於以下擴展庫目錄：
* 🔗 **實體腳本 GitHub 位置**：[MCP-Tools-extension / Standard.panel](https://github.com/CyberPotato0416/MCP-Tools-extension/tree/main/MCP_Tools.extension/MCP_Schedules.tab/Standard.panel)
