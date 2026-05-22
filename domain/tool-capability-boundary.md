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

### L6: override_element_graphics 在 Room 上 silent no-op（2026-05-22 新增）

| 項目 | 詳細說明 |
|------|------|
| **限制** | `override_element_graphics` 對 Room（房間）呼叫時，C# `view.SetElementOverrides()` 會回 `Success=true` 且 Transaction commit 成功，**但在 FloorPlan 視覺上不顯示任何顏色變化**——因為 Room 不是 3D 實體、沒有 Cut Geometry，`SetCutForegroundPatternColor` 雖然存入 OverrideGraphicSettings 但平面視圖無從套用 |
| **典型場景** | 想用顏色標記「FAIL 房間」做視覺戲劇效果（如 0523 demo 原 Step 5 的「染 3 間排煙 FAIL 房紅色」） |
| **辨識方式** | (a) API 回 `Success=true` 但使用者反映「畫面沒變化」；(b) 對象 ElementId 用 `get_element_info` 查回 Category=Rooms |
| **AI 應對策略** | 收到「染房間 / colorize rooms / 房間上色」請求時，**立即說明工具邊界**並提供兩條替代路徑：<br>(a) **染圍繞 Room 的牆**（用 query / get_room_info 取得 bounding wallIds，再對牆 override）<br>(b) **設計師在 Revit UI 設 Color Scheme**（View Properties → Color Scheme → 依參數分類）——脫離 MCP 範圍但是 Revit 給設計師的標準作法 |
| **更上游的判斷** | 「染 FAIL 房」這個需求本身可能就違反 slide 6-4「MCP 給 0/1、設計師走光譜」命題——把 AI 判定結果視覺化會搶走設計師的光譜決策。優先考慮改成「視覺化規範限制本身施加的位置」（如染 §45/§110 違規牆段），而不是「視覺化 AI 判定為 FAIL 的容器」 |
| **未來方案** | (i) `override_element_graphics` 對 Room 應主動 reject 並回 `Error: Rooms in plan view require a Color Scheme, not SetElementOverrides`；或 (ii) 新增 `apply_color_scheme_to_view` 工具，內部處理 Color Scheme 設定 |

**lesson 起源**：5/22 dry-run 0523 demo Step 5「染 3 間排煙 FAIL 房紅色」，API 全 Success 但 Revit 平面看不到變色。調查發現 5 個既有 skill（element-coloring / fire-safety-check / wall-orientation-check / parking-check / element-query）對 override_element_graphics 的對象都是有 3D 幾何的元素，Room 從未出現——Room 從一開始就不在工具設計範圍內，是 0523 handson 文件單方面假設了該支援。同日 redesign 改為「染 check_exterior_wall_openings 回的 violation 牆段」（規範限制可見化），同時避開 L6 工具邊界 + 對齊 slide 6-4 命題。

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

---

## 能力缺口 vs Revit 既有功能（2026-05-14 新增節，呼應 L-024）

前述 L1–L5 是「**MCP 工具的不可達邊界**」（連結模型查詢、類別解析、視圖範圍等技術限制）。本節補充另一條更上游的判斷：**並非所有能力缺口都該寫工具來補**——當 Revit 軟體本身已有功能時，AI 應指導使用者操作 UI，而非寫 redundant tool。

### 為什麼需要這條

Branch C（poisonsam fork 收編）盤點揭露：fork 老師對 Revit 軟體本身不夠熟時，會反覆寫出 redundant tools。以三個拒收的工具為證：

| 拒收工具 | Revit 既有功能 | fork 老師為什麼還是寫 |
|---|---|---|
| `update_wall_curve` | 拖拉牆 endpoint / 刪重建 | 對方腳本算錯座標想就地改——AI 自造的需求 |
| `auto_place_rooms` | 「自動置放房間」UI 按鈕 | 不知道 UI 已有此功能 |
| `update_category_line_weight` | Object Styles 對話框（管理 → 物件型式） | 不熟 Visibility / Graphic Overrides 完整三層機制 |

### Revit Visibility / Graphic Overrides 三層機制（範例）

設計師調整元件外觀，Revit 已有完整三層架構：

| 層 | 機制 | 作用域 | 對應既有 MCP tool |
|---|---|---|---|
| **L1** | Object Styles（管理 → 物件型式） | document-level（影響全部視圖） | 無（不該補，UI 表格化更直觀） |
| **L2** | Filter / View VG Overrides | per-view，條件式 filter | 無（複雜 filter 邏輯 UI 更直接） |
| **L3** | Element-level override | per-view per-element | ✅ `override_element_graphics`、`clear_element_override` |

**判讀**：L1/L2 走 UI（表格化、條件式設定 UI 更友好）；L3 是 per-element 精準操作 → AI 對話有 marginal value（從一堆元素中挑某幾個 override，UI 要逐個點，AI 一句話篩出來 override 更快）。**這就是為什麼 override_element_graphics 該收、update_category_line_weight 不該收的差別**。

### 工具設計三問（給未來想新增工具的人）

1. **Revit UI 已有同樣功能嗎？** 若有，marginal value 在哪？
   - UI 一鍵 = AI 對話一句 → marginal value = 0
   - UI 要逐個點 = AI 對話一句篩出條件 → marginal value > 0（如 `override_element_graphics`）
   - UI 沒此功能 = 真實能力缺口 → 可考慮開發
2. **BIM 設計師工作流真的需要嗎？** 還是 AI / 腳本自造的需求？
   - 用 use case 反推：「設計師沒 AI 也會這樣做嗎？」是 → 真實需求；否 → 自造需求（如 `update_wall_curve`）
3. **這工具能跟其他工具形成 workflow chain 嗎？**
   - 上游 tool 餵資料？下游 tool 接後處理？沒有 = single-shot tool，工作流斷在那裡 = 無意義
   - 範例：`auto_place_rooms` 後沒命名規則、沒篩選、沒採光鏈接 → workflow chain 不存在

### 三問都不通過時，AI 該做什麼

**指導使用者操作 Revit UI**，不是寫工具。範例對話模板：
- 「在 Revit 點 **管理 → 物件型式** → 在 [類別] 行的 [投影/切割] 欄改數字」
- 「在 Revit 點 **房間** 工具 → 工具列『自動置放房間』按鈕」
- 「在 Revit 視圖**滑鼠拖牆 endpoint**」

### 真有能力缺口時的正確路徑

**先上報 issue 給 maintainer 評估**，不要直接寫工具：
- 描述「我想做 X，Revit UI 沒有此功能 / UI 操作太繁瑣 / 純 AI workflow 需要」
- maintainer 評估是否符合「工具設計三問」+ 是否該編排到既有 Skill
- 通過評估再開 PR

這呼應「上報能力缺口而非繞道」原則——fork 老師的 AI 直接寫 .mjs 腳本繞 MCP / 直接寫 redundant tool 都是「自己擴張能力邊界」的反模式。
