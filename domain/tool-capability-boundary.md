---
name: tool-capability-boundary
description: "MCP 工具能力邊界定義表：定義目前 MCP 工具的不可達邊界（如連結模型元素不可查詢等），讓 AI 在收到相關請求時立即告知使用者限制而非反覆嘗試。當使用者提到連結模型、linked model、結構、能力邊界、boundary、找不到元素、0 結果時觸發。"
metadata:
  version: "1.0"
  updated: "2026-03-10"
  created: "2026-03-10"
  contributors:
    - "Admin"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by: []  # TODO: 月小聚補（被哪些 skill 引用）
  tags: [連結模型, linked model, 結構, structural, 邊界, 能力, boundary, 找不到元素]
---

# MCP 工具能力邊界定義表

## 目的

本文件定義目前 MCP 工具的**不可達**邊界，讓 AI 在收到相關請求時，**立即告知使用者**限制而非反覆嘗試，避免產生大量 .js 腳本或無效查詢。

---

## 分級

### L1: 連結模型元素不可查詢

| 項目 | 詳細說明 |
|------|------|
| **限制** | 目前 `query_elements`、`get_element_info`、`query_elements_with_filter` 等工具僅可查詢 **host document**，無法穿透 `RevitLinkInstance` 查詢連結模型內的元素 |
| **典型場景** | 結構模型（如 `*Structural.rvt` 等）掛在主機模型下；MEP 模型（`*MEP.rvt`、`*Plumbing.rvt`、`*HVAC.rvt`、`*Electrical.rvt`）的元素都不可查詢 |
| **辨識方式** | 使用 `query_elements({ category: 'RvtLinks' })` 確認有已載入連結模型存在，但在 host document 中以 0 筆結構構件、連結模型名稱包含 "Structural" 等特徵來判斷該元素屬於連結模型 |
| **AI 應對策略** | 回覆：目前連結模型 [名稱] 內的元素超出 MCP 工具的直接查詢範圍。建議使用者 (a) 在 Revit 中直接開啟連結模型進行查詢，或 (b) 開發 C# 擴充透過 RevitLinkInstance 查詢 |
| **未來方案** | 開發 `query_linked_elements` C# 擴充：使用 `FilteredElementCollector(doc, linkInstance.GetLinkDocument())` |

### L2: QueryElements 類別解析限制

| 項目 | 詳細說明 |
|------|------|
| **限制** | `query_elements` 的類別名稱僅支援 6 種預設英文名：`Walls`/`Rooms`/`Doors`/`Windows`/`Floors`/`Columns`，其餘類別需 `ResolveCategoryId` 動態解析 |
| **典型場景** | 使用 `ResolveCategoryId` 在 `doc.Settings.Categories` 中以名稱比對，非預設類別可能匹配失敗 |
| **辨識方式** | 使用者提及「不在預設清單中的類別」時，應先使用 `get_active_schema` 取得模型中所有類別的 **InternalName**（如 InternalName 為 `StructuralFraming` 而非 `Structural Framing`） |
| **AI 應對策略** | 優先嘗試 1 次正確的 InternalName，若 0 結果，考慮是否為 L1（連結模型）問題 |

### L3: 視圖範圍影響查詢結果

| 項目 | 詳細說明 |
|------|------|
| **限制** | `query_elements` 搭配 `viewId` 時，結果受該視圖的類別可見性（Category visibility）、視圖範圍（View Range）、階段篩選（Phase Filter）等因素影響 |
| **辨識方式** | 在不同視圖間查詢結果數量差異大時，使用 `get_active_schema` 比對各視圖的 Count |
| **AI 應對策略** | 切換視圖或移除 `viewId` 參數以使用全模型查詢來確認正確數量 |

### L4: 類型名稱 vs 實例名稱

| 項目 | 詳細說明 |
|------|------|
| **限制** | `get_column_types` 等工具回傳的是類型資料，而非實例級別的**位置或特定屬性值**。使用者常混淆兩者導致查詢不到結果 |
| **辨識方式** | 類型級查詢有結果，但實例級查詢卻為 0 |
| **AI 應對策略** | 回覆：此為模型中的[類型/型別]資訊，模型中已有該類型但可能尚未放置實例。需查詢實例級資訊請使用不同查詢方式 |

### L5: Schedule/報表資料不在 MCP 範圍內

| 項目 | 詳細說明 |
|------|------|
| **限制** | `get_all_views` 可列出 `ViewSchedule` 類型的視圖，但目前 MCP 工具無法讀取 Revit 明細表/報表的內容 |
| **未來方案** | 開發 `query_schedule_data` C# 擴充 |

---

## 緊急停止模式

AI 在執行過程中遇到以下模式時，**必須立即停止**而非繼續嘗試：

| 模式 | 觸發標準 | 範例 |
|------|------|---------|
| **類別名稱窮舉式搜尋** | 同一查詢已嘗試 2+ 次不同類別名稱卻無結果 | 先試 `Structural Framing` 後試 `StructuralFraming` 後試 `結構構架` |
| **視圖輪替式搜尋** | 同一查詢已在 2+ 個不同視圖中嘗試卻無結果 | 先試 Section 再試 3D 再試 FloorPlan |
| **腳本輪替式搜尋** | 本質上相同的邏輯已產生 2+ 個不同檔名的腳本 | 先寫 `check_fields.js` 再寫 `test_names.js` 再寫 `deep_search.js` |
| **零結果迴圈式搜尋** | 連續 3+ 次不同查詢都回傳 0 結果且無新資訊 | 每次查詢都是 Count: 0 且無新線索 |

---

## 維護規則

- 新增工具能力後，須更新對應 `L{N}` 條目，並標記為已解決或降級
- 每次發現新的工具邊界問題，須記錄至對應層級並更新觸發模式表
- Fix & Document Hook 適用：每次修復邊界後須同步更新 GEMINI.md、CLAUDE.md、CHANGELOG.md
