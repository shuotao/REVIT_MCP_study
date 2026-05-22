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

### L7: Tool Scope Mismatch（同批工具的回應範圍不一致，2026-05-22 新增）

| 項目 | 詳細說明 |
|------|------|
| **限制** | 同一個 prompt 並行 invoke 多個工具時，**這些工具的回應範圍可能不一致**——有的 project-wide（掃整案）、有的 level-scoped（吃 `levelName` 參數）。產出的混合報告會誤導使用者，以為所有結果都是同一範圍 |
| **典型場景** | 0523 demo Step 3「5 工具並行 ARCHI 檢查」：<br>• `check_exterior_wall_openings` → **project-wide**（不吃 level 參數，掃 445 牆全跑）<br>• `check_smoke_exhaust_windows` / `check_stair_headroom` / `get_room_daylight_info` / `check_floor_effective_openings` → **level-scoped**（吃 `levelName`/`level` 參數）<br>使用者在 2FL 跑這 5 工具，會拿到 4 份 2FL 報告 + 1 份整案報告，但統一呈現時看起來都像「2FL 的結果」 |
| **辨識方式** | (a) 工具 schema 中是否有 `levelName` / `viewId` / `level` 等 scope 參數；(b) 回傳的 JSON 是否有 `LevelName` / `ViewId` 欄位呼應 caller 的請求 |
| **AI 應對策略** | 並行 invoke 多工具時，**主動 surface 範圍差異**：「以下 5 個工具中，4 個是 X 樓層範圍、1 個是整案範圍。整案範圍的結果（例如 violation 8 項）跨越多樓層，**請勿假設它們都發生在當前樓層**」 |
| **更上游的問題** | 工具設計時應盡量讓同一類別的工具有統一的 scope 約定（要嘛全 project-wide，要嘛全 level-scoped）。混合 scope 是技術債，會在 demo / hands-on 練習時暴露 |
| **未來方案** | (i) 為 `check_exterior_wall_openings` 增加可選的 `levelName` 過濾參數；(ii) 或在所有工具回傳中強制加入 `ResultScope: "project" \| "level" \| "view"` 標籤，讓 caller 自動感知 |

**lesson 起源**：5/22 dry-run 在 2FL 跑 Step 3 五工具批次，發現 `check_exterior_wall_openings` 回了 8 項違規（含 z=0/100 的 1F 開口 + z=3170 的高樓層開口），混在「2FL 合規報告」中容易誤判。

### L8: Regulation Type → Coloring Strategy 對應（2026-05-22 新增）

| 項目 | 詳細說明 |
|------|------|
| **限制** | `override_element_graphics` 的染色策略**不能跨規範類型通用**——不同規範的「限制施加位置」不同，視覺化策略也不同 |
| **二分類** | **(A) Wall-anchored 規範**（限制施加在「牆上的特定開口/段落」）：直接染 violation 牆段。例：§45/§110 外牆開口距地界線。<br>**(B) Room-anchored 規範**（限制施加在「房間整體的某屬性」）：沒有「違規牆段」，需 proxy 染色。例：§41 採光、§101/§188 排煙、停車位淨高。 |
| **(A) Wall-anchored 染色 SOP** | 從 `check_exterior_wall_openings` 等回的 violation list 拆出唯一 wallId，依 status 染色（Fail 紅 / Warning 黃）。**這是 0523 handson Step 5 redesign 原版設計，對 §45/§110 完全成立** |
| **(B) Room-anchored 染色 SOP** | 三條 proxy 選擇：<br>(b1) **染 hosting walls**：從 `get_room_daylight_info` 拿房間 Openings 的 HostWallId 集合 → override 這些牆。表達「房間邊界」。5/22 dry-run 對事務室 §41 FAIL 走這條，5/5 牆成功視覺化<br>(b2) **染 bounding walls（更完整）**：用 `get_element_geometry` 取 Room boundary → 找所有圍合此 Room 的牆。比 b1 完整但需額外查詢<br>(b3) **染外殼開口位置**：標出「該層樓所有對外開口在哪」（用 `check_floor_effective_openings`），表達「整層的對外性脈絡」。對 §41 / §101 FAIL 都適用 |
| **AI 應對策略** | 收到「視覺化 FAIL」請求時，**先問：這是 wall-anchored 還是 room-anchored 規範？** 走錯分支會發生「Room override silent no-op」(L6) 或「染色對象跟規範語義對不齊」 |
| **未來方案** | (i) 在 `check_*` 系列工具回傳中加 `RegulationType: "wall-anchored" \| "room-anchored" \| "level-anchored"` 標籤；(ii) 新增 `override_room_boundary_walls(roomId, color)` 高階工具直接封裝 b1 |

**lesson 起源**：5/22 dry-run 在 2FL 跑事務室（§41 採光 0% FAIL），原 Step 5 redesign 的染牆 prompt 不直接適用——事務室沒有「違規牆段」，必須用 hosting walls proxy。

### L9: MCP Failure Mode & Recovery（2026-05-22 新增）

| 項目 | 詳細說明 |
|------|------|
| **限制** | MCP 工具呼叫可能 timeout、無回應、或返回 error，原因包括：Revit UI thread 被 modal dialog 阻塞、ExternalEventManager queue 卡住、HttpListener 死掉、Revit 被關閉、port 8964 被 HTTP.sys 孤兒 queue 佔用等 |
| **典型徵兆** | (a) 工具呼叫超過 30 秒無回應；(b) 連續多次同一工具 timeout；(c) Tool error: "Connection refused" / "Connection reset" |
| **AI 應對 SOP（依嚴重度遞增）** | 1. **第一次 timeout**：等 5 秒後重試一次（可能是 Revit 暫時忙）<br>2. **第二次 timeout**：停止重試，**不假裝知道模型狀態**，立刻 surface 給使用者（Tool Call Data Honesty Branch C）<br>3. **連續 3 次以上**：建議使用者跑 diagnostic 步驟（見下） |
| **使用者端 diagnostic 順序** | (a) Revit 視窗還開著嗎？UI 正常嗎？<br>(b) Revit 內 RevitMCP 面板的 Server 燈號還是綠的嗎？<br>(c) Revit 有彈出任何 modal 對話框擋著嗎？<br>(d) 若 (a)(b)(c) 都正常但仍 timeout → Revit 點任意視圖一下，重新確立 active focus<br>(e) 仍不行 → RevitMCP 面板按「Restart Server」<br>(f) 仍不行 → 關 Revit 重開<br>(g) Port 8964 被佔用 → 跑 `scripts/release-port.ps1`（需管理員權限） |
| **5/23 demo 講者預備** | Live demo 中 MCP 中斷是真實會發生的事。講者應預演 (d)(e) 兩步驟，並有 fallback 影片可即時切換 |
| **未來方案** | (i) MCP-Server 端加 health-check / heartbeat；(ii) Tool timeout 後自動嘗試 RestartServer；(iii) RevitMCP 面板顯示連線狀態 LED + 最後一次成功 ping 時間戳 |

**lesson 起源**：5/22 dry-run 中段，連續 2 次 `get_active_view` timeout。AI 拒絕假裝知道視圖狀態繼續執行 override（Branch C 啟動），等使用者修復連線後 re-anchor。修復方式是使用者在 Revit 點一下視圖（隱式 active focus 重建）。

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
