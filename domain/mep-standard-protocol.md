---
title: MEP 採購與點料明細表標準化協議 (MEP Procurement & On-site Schedule Protocol)
description: 定義機電專案中「管」、「管配件」、「管附件」的標準化明細表產出規範，確保欄位對齊採購與現場監工需求。
category: 報表與明細表 (Schedules & Reports)
status: 已驗證 (Verified)
version: 1.1
---

# MEP 採購與點料明細表標準化協議 (V1.1)

本協議定義了如何利用 MCP 工具生成「標註級別」的明細表，旨在銜接 Revit 模型資料與現場採購/點料作業。

## 🎯 目標
1.  **規格精確化**：統一「族群」與「類型」的表達方式。
2.  **商務鏈接**：自動納入「製造商」與「工項編碼」。
3.  **現場識別**：確保提供「外徑」與「描述」等關鍵現場比對資訊。

## 📋 各品類標準欄位定義

### 1. 管 (Pipes)
*   **用途**：計算管材總長、確認現場管外徑、對號入座發料。
*   **標準欄位順序**：
    1.  `標記` (Mark)
    2.  `製造商` (Manufacturer)
    3.  `系統類型` (System Type)
    4.  `族群` (Family)
    5.  `類型` (Type)
    6.  `大小` (Size) - 標稱直徑
    7.  `外徑` (Outside Diameter) - **關鍵現場比對欄位**
    8.  `長度` (Length)
    9.  `參考樓層` (Reference Level)
    10. `工項編碼` (Assembly Code)
    11. `備註` (Comments)

### 2. 管配件 (Pipe Fittings)
*   **用途**：採購彎頭、三通、管帽等配件。
*   **標準欄位順序**：
    1.  `標記` (Mark)
    2.  `製造商` (Manufacturer)
    3.  `族群` (Family)
    4.  `描述` (Description) - **關鍵採購敘述**
    5.  `大小` (Size)
    6.  `數量` (Count)
    7.  `工項編碼` (Assembly Code)
    8.  `樓層` (Level)
    9.  `備註` (Comments)

### 3. 管附件 (Pipe Accessories)
*   **用途**：管理閥件、儀表等高價值設備。
*   **標準欄位順序**：
    1.  `標記` (Mark)
    2.  `製造商` (Manufacturer)
    3.  `系統類型` (System Type)
    4.  `族群` (Family)
    5.  `類型` (Type)
    6.  `描述` (Description)
    7.  `大小` (Size)
    8.  `數量` (Count)
    9.  `工項編碼` (Assembly Code)
    10. `樓層` (Level)
    11. `備註` (Comments)

## 🛠 自動化生成流程 (AI Workflow)

### 第一步：環境探索
必須先執行 `get_active_schema()` 與 `get_category_fields()` 以確保目標品類的欄位名稱與本協議定義的字串 100% 匹配。

### 第二步：執行建立
AI 應根據本協議定義的順序，呼叫 `create_view_schedule`。若品類不存在目標欄位，應在日誌中記錄並跳過，不可強行推測。

## ⚠️ 版本復盤紀錄 (Lessons Learned)
*   **V1.0 (2026-03-17)**：初始版本，僅包含基礎欄位。
*   **V1.1 (2026-03-17)**：修正「管」類別應將族群與類型拆分，並為所有品類補齊「製造商」與「工項編碼」以符合採購需求。

---
最後更新: 2026-03-17 (V1.1)

## 📸 實作驗證 (Implementation Verification)

已透過 pyRevit 插件成功實作自動化生成流程：

| 介面執行結果 | 專案瀏覽器生成 |
| :--- | :--- |
| ![成功彈窗](../docs/images/success_dialog.png) | ![明細表列表](../docs/images/project_browser.png) |

**執行日誌紀錄：**
![日誌細節](../docs/images/output_log.png)
