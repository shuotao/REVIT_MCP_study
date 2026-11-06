---
name: tool-capability-boundary
description: "MCP 工具能力邊界定義表：定義目前 MCP 工具的不可達邊界（如連結模型元素不可查詢、Revit UI API／第三方外掛按鈕觸發不可達、Space 品類無工具支援等），讓 AI 在收到相關請求時立即告知使用者限制而非反覆嘗試。當使用者提到連結模型、linked model、結構、能力邊界、boundary、找不到元素、0 結果、pyRevit、UI API、按鈕觸發、PostableCommandId、Reload、Space、機電空間、明細表公式、條件格式、ArchwizPF、整合平台 Lite、Archicad MCP 接入時觸發。"
metadata:
  version: "1.4"
  updated: "2026-10-10"
  created: "2026-03-10"
  contributors:
    - "Admin"
  references:
    - "https://github.com/shuotao/REVIT_MCP_study/issues/110"
    - "https://github.com/shuotao/REVIT_MCP_study/issues/98"
    - "MCP/Core/CommandExecutor.cs:4119（create_view_schedule 品類名稱解析）"
    - "https://archwiz.net/store（Archwiz 整合平台 Lite 商品/下載入口；非 MCP endpoint）"
  related:
    - mep-extension-guide.md
    - mep-space-demand-matrix.md
  referenced_by:
    - element-coloring
    - archicad-skill-adapter
  tags: [連結模型, linked model, 結構, structural, 邊界, 能力, boundary, 找不到元素, pyRevit, UI API, PostableCommandId, 按鈕觸發, Reload, Archicad, ArchwizPF, Lite, MCP, backend, namespace, Space, MEPSpaces, 機電空間, schedule, 明細表, Calculated Value, Conditional Format]
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

### L5: Schedule/報表資料不在 MCP 範圍內 ⚠️ 已大部分解除（2026-08-11 更正）

| 項目 | 詳細說明 |
|------|------|
| **原限制（已過時）** | 原文寫「MCP 工具無法讀取 Revit 明細表/報表的內容」。**此敘述已不成立** |
| **現況** | `MCP-Server/src/tools/schedule-tools.ts` 已提供 `list_schedules`（列出明細表）、`read_schedule`（讀取明細表內容）、`create_view_schedule`（建立明細表）。明細表的**列出、讀取、建立骨架**都在 MCP 範圍內 |
| **仍不可達的部分** | (a) **Calculated Value 公式**——Revit API 無公式 setter，只能在 Revit UI 的 Fields → Add Calculated Parameter 建立；(b) **Conditional Format**（條件格式／逐格標色）——同樣無 API setter，只能 GUI 設定；(c) **新增專案參數／共用參數**——目前無對應工具，明細表要用的自建欄位須 GUI 建立 |
| **AI 應對策略** | 收到「用明細表做自動檢核」類請求時，正確回答是「骨架與讀回可自動化，**公式與標色必須手動**」，而不是「明細表不在 MCP 範圍」。SOP 必須把 (a)(b)(c) 明確寫成手動步驟 |
| **更正起源** | 2026-08-11 為 `domain/mep-space-demand-matrix.md` 盤點載體時發現本條與原始碼不符。原條目應為 2026-03 撰寫時的真實狀態，`read_schedule` 等工具為其後加入，本條未同步更新 |

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

### L10: Revit UI API／第三方外掛 UI 命令觸發不可達（2026-08-10 新增，源自 issue #110）

| 項目 | 詳細說明 |
|------|------|
| **限制** | 本專案的 bridge 走 `MCP/Core/ExternalEventManager.cs` + Revit **DB API**（Element / Parameter / View / Transaction 等模型資料操作）。**不提供** Ribbon 按鈕觸發、`UIApplication.PostCommand()` 這類 UI API 呼叫、也不代理第三方外掛（如 pyRevit）動態註冊的 UI 命令 |
| **典型場景** | 使用者想讓 AI「觸發 pyRevit 按鈕」「呼叫 pyRevit → Reload」「透過 MCP 驗證某個 pyRevit `.py` 腳本改完有沒有生效」，省去手動點 Revit 介面的步驟 |
| **技術事實（issue #110 的 OSI 分析判斷正確，予以承認）** | (a) **L5↔L6 斷層**：Ribbon 按鈕觸發必須發生在 Revit UI Thread；MCP 目前透過 `ExternalEventManager` 只進到 L5（Session）進入點，沒有建立 L6/L7（Presentation/Application）介面，因此無法呼叫 `PostCommand()`。(b) **L6↔L7 斷層**：pyRevit 的按鈕是**動態載入並註冊**的 `IExternalCommand`，不像 Revit 原生命令有固定 `PostableCommandId` 可供外部 API 呼叫——這是 pyRevit 自身的動態註冊機制所致，**不是本專案 bridge 的缺陷，本專案也修不了** |
| **為什麼不收（維護者裁決，2026-08）** | 提案方案 A（MCP 直接調用 pyRevit 的 IronPython/CPython 執行引擎）會把第三方外掛的執行引擎接進核心 bridge——提案人自己也指出這牽涉 pyRevit 開源／授權問題，風險認定正確。方案 B（C# 端反射取得 pyRevit 註冊的 `RevitCommandId` 再 `PostCommand()`）對 **pyRevit 版本與 Revit 版本雙重脆弱**：任一邊升級都可能讓反射目標消失或改名。本專案要同時維護 R22–R26 五個 build config（見 CLAUDE.md「Build Commands」），把這種雙重版本脆弱的相容性負擔接進核心，成本與工具價值不成比例 |
| **AI 應對策略** | 收到「觸發 pyRevit 按鈕」「MCP 幫我按 Reload」「用 MCP 呼叫某個外掛命令」類請求時，**立即說明這是 UI API 範圍外**，不要嘗試繞道（例如自行組 WebSocket payload 呼叫 `UIApplication`，這違反 CLAUDE.md「Do Not Bypass MCP」guard rail），也不要反覆嘗試不同工具名稱去猜 |
| **替代路徑（正解）** | 若目的其實是「驗證某段 pyRevit 腳本邏輯有沒有效」，**不需要隔著 UI 按鈕測**——把該段邏輯**收編成 MCP 工具**直接呼叫、直接斷言結果，跳過 UI 觸發這一層。本專案已有不少工具就是這樣從 pyRevit 生態圈收編而來（研究路徑與案例見 `domain/mep-extension-guide.md`）。具體做法：先描述「我想驗證的邏輯是 X」，再依既有工具設計流程（例如已封裝獨立 `.cs`/DLL 命令時走 `dll-to-mcp-tool` skill）把邏輯包成可從 MCP 直接呼叫、可用回傳值斷言結果的工具，而不是驅動 UI 按鈕再靠人眼確認畫面 |
| **未來方案** | 若 Revit API 或 MCP 官方 SDK 未來釋出穩定、跨版本相容的 UI 命令 orchestration 介面（而非反射 hack），可重新評估收編；在此之前維持不收 |

**lesson 起源**：issue #110（CyberPotato0416，2026-08-01 提出）。提案人以 OSI 七層模型精確定位出 L5↔L6、L6↔L7 兩處介面斷層，根因判斷（pyRevit 按鈕動態註冊、無固定 `PostableCommandId`）正確，分析品質值得肯定。維護者裁決此為範圍外（不落地方案 A/B），但保留其技術分析價值，並在此記錄替代路徑（邏輯收編為 MCP 工具），供未來類似請求參考。

### L11: 多 Backend Namespace 隔離（Revit / Archicad）

| 項目 | 詳細說明 |
|------|------|
| **限制** | Revit MCP 與 Archicad MCP 是兩個獨立 server。Revit `ElementId`、Archicad element GUID、Archicad instance port、類別／元素型別、參數／Property、內部單位與座標系統都不是可直接互換的 namespace。 |
| **典型場景** | 使用者希望把既有 Revit-oriented Skill 套到 Archicad，或同一個 AI Client 同時看得到 `revit-mcp` 與 `archicad-mcp`。 |
| **辨識方式** | 執行鏈開始前先確認目標 application；Archicad 需在本 turn 取得 live instance port，Revit 需依 active-state re-anchoring 取得當前 document/view。每個 identifier 都保留來源 backend。 |
| **AI 應對策略** | 保留 Domain 的 BIM 方法，但透過 backend adapter 重新 discovery 工具與 schema。不得把 Revit tool name 當成 Archicad command，不得把 ElementId 改名為 GUID，不得沿用另一個 Archicad port 的結果。 |
| **單位邊界** | Revit internal feet 的換算規則不能自動套到 Archicad。Archicad arguments 與結果一律依當次 discovery 回傳的 schema／說明判讀；未標示單位時停止並要求確認。 |
| **寫入驗證** | 兩個 backend 的 mutation 都必須以同一 backend 的 read-back 驗證。Archicad 寫入以本 turn 選定 port + GUID 回讀；Revit 仍依既有 MCP tool 與 Transaction 邊界驗證。 |
| **能力缺口** | 若 Archicad discovery 找不到 Domain 某一步所需能力，標示該步 `unsupported` 並停止該 mutation，不得改呼叫 Revit 工具補做，也不得猜測 raw JSON API payload。 |
| **安裝邊界** | Repository 預設 config 維持 Revit-only。只有使用者主動 opt in 才加入獨立的 `archicad-mcp` entry；停用時只移除該 entry。 |

**lesson 起源**：issue #98（Archwiz-boss，fork `Archwiz-boss/BIM_MCP_study`，commit `998adce8`）。原編號在 fork 分支上是 `L10`，但該分支自 2026-07-22 起未再更新，同一時段本檔已把 `L10` 用於 issue #110（Revit UI API／第三方外掛 UI 命令觸發不可達）。維護者裁決 2026-08 全數收編 issue #98 的 Skill／Domain 內容，此節在收編時重編號為 `L11` 以避免覆蓋既有 L10，內容本身逐字保留 fork 原文。收編僅涵蓋 Domain 知識與 Skill 編排文件，不含 fork 的 `.mcp.json`／`.vscode/mcp.json`／setup 腳本；相關的可攜性狀態與零實測證據說明見 `docs/integrations/archicad-skill-portability.md`。

<a id="archwizpf-lite"></a>

#### ArchwizPF Lite：獨立 MCP 的接入與能力邊界

**Archwiz 整合平台 Lite（ArchwizPF）**是 Archicad 的中文面板與內建 MCP 伺服器，與本 repo 的 Revit bridge、Tapir Add-On 及其 MCP provider 分別獨立。它不依賴 Tapir；不能把 Tapir 的 discovery/call 工具當作 `archwizpf_` 工具入口。既有 Skill 的工具流程可能綁定 Tapir，使用 Lite 時應明確指定本節與 `archwizpf` provider，再依已安裝 schema 映射。本節補充知識與編排路徑，沒有修改 Skill、實作 MCP、設定用戶端或操作模型，也不是實機驗證紀錄。載入本節或 `archicad-skill-adapter` 不表示所有 Domain／Skill 方法已在 Lite 執行或全部工具已自動載入。

##### 四個開發版本與驗證範圍（2026-10-10 快照）

目前產品版本為 `0.1.0`，平台為 `windows-x64`，仍在開發中、尚未發行。範圍為 **Archicad 25、26、27、28 四個版本**；不承諾 29 或 macOS。下表依產品版本紀錄與維護者回報整理，編譯、載入與完整功能驗收是不同層次。

| Archicad | 已知建置／實機證據 | 使用限制 |
|---|---|---|
| 25 | DevKit 25.3002 編譯；舊側邊欄建置在 Archicad 25 build 4013 載入、連線測試與面板通過 | 內建 `API.GetSelectedElements` 不支援，故 `archwizpf_get_selected_elements` 不可用；暫態高亮不可用 |
| 26 | DevKit 26.7000 編譯；更早單頁面板在 build 4019 載入與指令往返通過；側邊欄版尚未重新載入驗證 | 暫態高亮的 API 版本門檻符合，但不代表新工具已實機通過；Keynote 不支援 |
| 27 | DevKit 27.6003 編譯；更早單頁面板在 build 3001 載入與指令往返通過；側邊欄版尚未重新載入驗證 | 新功能仍待移植／驗證；Keynote 不支援 |
| 28 | DevKit 28.4001／28.3001 編譯；舊側邊欄建置在 build 3001 載入、連線測試與面板通過 | 本範圍唯一有 Keynote API 的版本；其工具仍待實機驗證 |

完整工具與新增功能頁目前先在 28 開發，25–27 保留較早建置；**四版不保證同等完整功能**。28 的實機驗證逐項進行中，已有部分面板／連線／寫入開關／圖庫項目通過；面板驗證與 MCP 工具呼叫驗證須分別看待。25、28 的上述面板證據不能套用到後續所有功能頁或 26 個工具，有工具卡片也不證明該工具已呼叫通過。

##### 商品入口與使用者接入

[商品/下載入口：Archwiz 商店](https://archwiz.net/store)。這是商店頁面，不是直接 ZIP 連結、遠端 MCP endpoint，也不表示已確認商品上架、購買或下載成功。

取得對應版本的發行 APX 後，由使用者關閉 Archicad、手動放入該版本的 `Add-Ons` 資料夾，或透過 Add-On Manager 載入；重新開啟後從「ArchwizPF → 整合平台 Lite」進入面板。APX 內含本機 stdio MCP server `archwizpf`，開啟面板時解出 `%LOCALAPPDATA%\ArchwizPF\archwizpf-mcp.exe`，再透過本機 Archicad JSON API 溝通。使用發行包不需另裝 Tapir、uv 或 Python。

安裝 APX 與註冊 AI 用戶端是兩步。使用者另在「MCP 連線設置」選用戶端、按「一鍵安裝」才修改其設定；Claude Code 依面板提供的命令自行註冊。之後重啟用戶端，查看 MCP 工具清單並測試連線。閱讀知識文件不授權 AI 自動改設定，也不保證當前 client 已連線。

##### 工具盤點：24 個讀取／暫態工具與 2 個模型寫入工具

以下是原始工具登錄的名稱；實際可呼叫範圍仍以已安裝版本的 MCP 工具清單、schema 與狀態為準。除下列兩個建立 Zone 工具外，不修改專案資料；選取、視圖與高亮仍有暫態副作用。

| 群組 | 工具名稱 | 效果與邊界 |
|---|---|---|
| 狀態與 instance（3） | `archwizpf_get_status`、`archwizpf_list_instances`、`archwizpf_select_instance` | 取得版本／build／port／APX 相容性；列出本機 instance；選定本次 server 連線目標，非模型寫入 |
| 專案、元素、Property（6） | `archwizpf_get_project_info`、`archwizpf_count_elements`、`archwizpf_get_elements`、`archwizpf_get_selected_elements`、`archwizpf_list_properties`、`archwizpf_get_property_values` | 基本專案資訊（非完整「專案資料」頁）、類型數量、GUID 與屬性；選取 GUID 工具在 25 不支援；專案資訊需 APX |
| 暫態視覺回饋（6） | `archwizpf_change_selection`、`archwizpf_highlight_elements`、`archwizpf_clear_highlight`、`archwizpf_focus_elements`、`archwizpf_restore_view`、`archwizpf_clear_feedback` | 需 APX；選取、聚焦與還原視圖，26–28 可暫態高亮；高亮非持久圖形覆寫。聚焦可切換視窗／樓層，`show_selection_in_3d` 會改選取 |
| 模型資料查詢（6） | `archwizpf_get_bounding_boxes`、`archwizpf_find_collisions`、`archwizpf_get_selected_zones`、`archwizpf_get_selected_polylines`、`archwizpf_get_selected_openings`、`archwizpf_get_structural_elements` | 需 APX；外接範圍、兩組碰撞、選取的 Zone／折線／門與開口、柱梁板資料；openings 不含窗。各工具有範圍、截斷與幾何限制 |
| Keynote（3） | `archwizpf_list_keynotes`、`archwizpf_find_keynotes`、`archwizpf_get_keynote_autotext` | 本四版範圍僅 28，需 APX；讀取／搜尋／取得 autotext token，不建立或修改 Keynote，不放置文字或標籤 |
| 建立 Zone（2） | `archwizpf_create_zone`、`archwizpf_create_polygon_zone` | **修改專案**：目前樓層／平面圖建立矩形或多邊形 Zone，可附既有自訂文字屬性；不修改既有 Zone |

寫入工具會出現在 MCP 清單，但 APX 面板的寫入開關**預設關閉**；只能由使用者在面板開啟並確認，AI 參數與環境變數不能繞過。`write_disabled` 要告知使用者並停止重試。建立行為有一步 Undo 與本機稽核；仍須在使用者授權範圍內操作並回讀。Lite 沒有既有 Zone 重新編號／通用 Property 寫入工具，不能直接執行 Revit 的房間編號方法。

「專案資料」「電子申請書」及其匯入／匯出是面板功能，沒有對應 MCP 工具；不能自動驅動面板按鈕。這些頁面的個資不進 MCP 工具輸出。模型與屬性讀取仍應限定當次需求；本機連線不表示 AI 用戶端不會把工具結果送給所選 AI 服務。

##### 可執行的知識路徑

1. 起始呼叫 `archwizpf_get_status`，確認 server、Archicad version/build/port、APX loaded/compatible；`ok=true` 本身不代表 APX 相容。多 instance 或目標不明時才用 `archwizpf_list_instances` → `archwizpf_select_instance` → 再確認 status。重開 Archicad 後重新錨定，不沿用舊 port。
2. 查看**當前 MCP provider 的工具清單與 input schema**，確認所需 `archwizpf_` 工具、參數、單位與限制。Tapir provider 的 discovery/call 不適用於 Lite；不能猜 raw JSON API 或用另一個 backend 補做。每一份 GUID、Property 與結果保留 provider + instance 來源。
3. 先讀對應 Domain 的方法，再取該步需要的最少資料。保持同一 provider、同一 instance 與一致選取／樓層範圍；缺工具或必要欄位時標示該步 unsupported／資料不足，停止依賴該步的計算或寫入。

三個小型工作流建議（均不寫入模型；視覺回饋另有暫態變更）：

| 需求 | Lite 路徑 | 完成界線 |
|---|---|---|
| 元素與屬性盤點 | status → 按類型 `get_elements` 或在支援版本用 `get_selected_elements` → `list_properties` → `get_property_values`（工具名均加 `archwizpf_`） | 用回傳的 nonLocalizedName 或 `[群組, 名稱]`，不猜 Revit 參數名；列出未知屬性／不可讀 cells，不把缺值當 0 |
| 選取 Zone 的幾何與面積資料 | status → `archwizpf_get_selected_zones` → 按回傳 GUID 與已查得屬性名讀 `archwizpf_get_property_values` | `outline_area` 是外框直線多邊形面積，非 Archicad 量測 Zone 面積；`has_holes`／`has_arcs` 表示資料不能完整重建。若進入容積檢討，回到 [floor-area-review.md](floor-area-review.md) 的分類／計算方法，先確認所需資料與本案法規，Revit 工具步驟須另映射，不能宣稱整個方法已執行 |
| 碰撞與暫時定位 | status → 明確兩組 GUID → `archwizpf_find_collisions` → 按需求 `archwizpf_focus_elements`／26–28 `archwizpf_highlight_elements` → `archwizpf_clear_feedback` | 回報兩組範圍、容許值、本體／淨空結果；結束時清除高亮並還原視圖。預設不清選取，需恢復原選取時先取得可回讀的原 GUID，不能宣稱 clear_feedback 已自動恢復選取 |

##### 結果完整性與單位

- `archwizpf_get_elements`：`offset` + `returned` 是下一頁起點，`truncated=true` 表示尚有頁；沒有 `next_offset`。`archwizpf_get_structural_elements` 才使用 `next_offset`，**`-1` 表示末頁**。選取 GUID 工具與其他截斷結果沒有通用續頁規則，須按各 schema 縮小範圍，不把單頁當全案。
- Property 回傳保留 Archicad JSON 的型別／值，工具契約標示長度為公尺；依屬性的物理量確認面積、體積等語義，不套 Revit internal feet、不把文字或 enum 當幾何量。幾何查詢的長度／座標為公尺、專案座標，結構角度為弧度；碰撞 `volume_tolerance_m3` 為 m³、`surface_tolerance_m` 為 m。Zone 外框面積與量測面積不得混用。
- 檢查 `ok`、`error`、`unknown_properties`、cell `status`、`failures`／`failed_guids`、`unsupported`、`truncated`。碰撞上限截斷、Zone 孔洞／弧線省略、折線弧線資訊省略與結構不支援形態均須列明；部分成功不是完整驗收。不支援時停止該路徑，錯誤時轉述回傳 message／next_step，不反覆猜工具。
- 視覺回饋以回傳的成功／失敗與 cleanup 每一步結果判定。`restore_view` 沒有已儲存視圖可回復時可成功但 `restored=false`；不能據此宣稱已恢復。臨時選取／視角不等於模型內容變更或持久上色。

當次驗證可用下列空白紀錄；它是模板，**沒有已執行的結果**，不要填入虛構 GUID 或專案資料：

```text
provider / server version:
Archicad version / build / port:
APX loaded / compatible:
Domain 方法與實際完成步驟:
工具 / 參數範圍 / 單位:
回應 ok / 頁數與完整性 / unsupported 或 errors:
暫態變更 / cleanup 與 read-back 結果（未執行則明記）:
未完成項目 / 尚待實機驗證:
```

### L12: Space（機電空間）無**專用**工具，但通用工具可用（2026-08-11 新增，同日經實測大幅更正）

> ⚠️ **本條初版宣稱「MCP 完全不支援 Space 品類」——該敘述不正確，已於同日實測推翻。** 保留原始敘述與更正過程，作為「靜態掃碼推論 ≠ 實際能力」的教訓。
>
> **初版的錯誤推論**：掃過本 repo 全部 `.ts` 與 `.cs`，`MEPSpaces` / `OST_MEPSpaces` 零命中，遂推論工具集不支援 Space。
> **錯在哪**：本專案多數通用工具以**品類名稱字串**解析（`doc.Settings.Categories` 逐一比對），**不是白名單**。原始碼中沒有出現某品類的識別字，不代表該品類不可用。同樣的誤判先前也發生在 `create_view_schedule` 上（見 L-E）。

**2026-08-11 實測結果（Revit 2026，69 個 Space 的模型）**：

| 動作 | 結果 |
|---|---|
| `query_elements_with_filter {category:"Spaces"}` | ✅ **可用**，回傳 69 筆完整資料 |
| 可取得欄位 | `ElementId`、`Number`、`Name`、`Level`、`Area`、`Volume`、`Unbounded Height`、`Upper Limit`、`Limit Offset`、**`Room Number`／`Room Name`**、`Plenum`、`Occupiable`、自建專案參數等 |
| `modify_element_parameter` 寫入 Space 參數 | ✅ **可用**（實測寫入 **42** 筆：`MEP_Tag` 26 筆＋`MEP_驗收狀態` 16 筆） |
| `create_view_schedule {category:"Spaces"}` | ✅ 可用（見 L-E） |
| `read_schedule` 讀 Space 明細表 | ✅ 可用 |

| 項目 | 詳細說明 |
|------|------|
| **實際限制** | 無 Space **專用**工具。所有房間**專用**工具（`get_room_info`、`get_rooms_by_level`、`renumber_rooms_by_level`、`batch_set_room_height`、`get_room_surface_areas`、`create_room_filled_regions` 等）走的是 `Rooms`（建築房間），**不會**作用於 `Spaces`（機電空間） |
| **仍不可達** | 建立 Space 實例（須 Revit UI：`Analyze → Spaces & Zones`）、新增專案參數、Calculated Value 公式、Conditional Format |
| **為什麼重要** | Room 與 Space 不是同義詞。Space 是機電元件，連結建築後讀 Room 邊界，並帶有 Room 沒有的機電欄位——其中 `Actual Supply Airflow`（實際送風量）**只長在 Space 上**。「實際送風量 vs 法定通風量下限」這類逐室自動檢核，因此**只能在 Space 上做，Room 做不到** |
| **典型場景** | 使用者想做逐空間通風量檢核、HVAC 分區、能源模型、空間需求矩陣時，會需要 Space |
| **辨識方式** | 使用者提到「Space」「機電空間」「實際送風量」「Actual Supply Airflow」「Spaces & Zones」「System Zone」時，**不要**改用 room **專用**工具代替——兩者是不同品類，代替會給出錯誤結果。應改用**通用**工具並指定 `category:"Spaces"` |
| **AI 應對策略** | (a) 查詢與寫入用通用工具（`query_elements_with_filter`、`modify_element_parameter`），指定 `category:"Spaces"`；(b) 建立 Space **實例**、新增專案參數、Calculated Value 公式、Conditional Format 四項須在 Revit UI 手動完成；(c) 正確說法是「**查詢、寫入、明細表建立與讀回皆可自動化；建立實例與四項 GUI-only 項目須手動**」，不是「Space 完全不能碰」 |
| **給未來的通則** ⭐ | **靜態掃碼找不到某品類的識別字，不足以推論該品類不可用。** 本專案多數通用工具以品類**名稱字串**解析（逐一比對 `doc.Settings.Categories`），非白名單。判定某品類能否使用**必須實測**——成本極低（一次查詢），而誤判的代價是整條工作流被錯誤地判定為不可自動化 |
| **未來方案** | 若逐空間工作流成為常態需求，可考慮新增 `get_spaces_by_level` / `get_space_info` C# 命令，與既有 room 工具並列而非取代 |

**lesson 起源**：2026-08-11 規劃 `domain/mep-space-demand-matrix.md` 時，以靜態掃碼推論 Space 不受支援，寫下本條初版；同日實跑 Tag 分類時實測發現查詢與寫入均可用，遂大幅更正。

**本條的價值不在 Space 本身，而在推論方法。** 同一個錯誤在本檔內發生過兩次——L-E 的 `create_view_schedule`、本條的 `query_elements_with_filter`——成因相同：**把「原始碼裡沒看到」當成「做不到」**。兩次都由實測推翻。往後對「某品類／某能力是否可用」的判斷，一律以實測為準，掃碼結果僅供形成假設。

### L13: `set_project_units` 是整份重建，不是合併（2026-08-11 新增）

| 項目 | 詳細說明 |
|------|------|
| **限制** | `set_project_units` 內部以 `new Units(baseSystem)` 建立一份**該系統的全新預設單位設定**，再 `doc.SetUnits(units)` 整個覆蓋文件既有設定（`MCP/Core/Commands/CommandExecutor.ProjectUnits.cs:59, 81`）。它**不是**在既有設定上做局部修改 |
| **可指定的範圍** | 只有 4 個 spec：`Length`、`Area`、`Volume`、`AirFlow`（同檔 72–75 行）。`mode=taiwan` 額外做的事只有把 `AirFlow` 設為 m³/h（65–69 行） |
| **保留值（精度）完全不支援** | 使用 `new FormatOptions(unitId)`，採該單位預設精度；全檔未呼叫 `SetAccuracy()`，也未開放任何精度參數。小數位數無法由工具設定，且既有精度設定會被一併重設 |
| **副作用** | 上述 4 個 spec 以外的所有單位設定——壓力、管徑、密度、坡度、流速、溫度等——**都會被靜默重設為該系統預設**，無任何提示 |
| **對單位制混用地區的具體風險** | 台灣機電實務壓力用 **kg/cm²**（工程制）、管徑常並列英吋。這兩者既不在可指定範圍內，又會被此工具重設掉。使用者以為只是「切公制」，實際上同時破壞了刻意配置的設定 |
| **辨識方式** | 呼叫前後比對 `Manage → Project Units` 中**未被指定的 discipline**；工具回傳的 `Result` 只報 4 個 spec，不會揭露其他被重設的項目 |
| **AI 應對策略** | (a) 呼叫前先請使用者確認現況、或明確警告「4 個 spec 以外的單位與所有精度設定都會被重設」；(b) 呼叫後不得宣稱「單位已統一」——只能說這 4 個 spec 已設定，其餘須人工確認；(c) 模型若已有完成的管線尺寸計算，先警告可能擾動 Mechanical Settings 與 Segments and Sizes；(d) 提醒為單一 Transaction，Ctrl+Z 可還原 |
| **未來方案** | 改為讀取既有 `doc.GetUnits()` 後只覆寫指定 spec（合併語意），並開放 `accuracy` 參數與更多 spec（壓力、管徑、流速等） |

**lesson 起源**：2026-08-11 討論 MEP 前期「單位盤點 → 統一 → 凍結」關卡時，為確認工具實際能力而讀原始碼發現。既有 skill 文件雖有「全案性動作」的警告，但未說明它是**整份重建**、也未說明**精度不受控**——這兩點才是真正會造成無聲破壞的地方。方法論面的處置見 `domain/mep-space-demand-matrix.md` 第 5-1 節「單位凍結」。

**L13 實測驗證（2026-08-11，Revit 2026 課程模型）**：在英制模型上執行 `set_project_units {mode:"taiwan", length:"mm", area:"m2", volume:"m3", airFlow:"m3/h"}`，前後比對確認——

| 項目 | 切換前 | 切換後 | 在可指定的 4 spec 內 |
|---|---|---|---|
| 空氣密度 | `0.08 lb/ft³` | `1.20 kg/m³` | ❌ 否 → **被重設** |
| 空氣黏度 | `0.02 cP` | `0.00002 Pa-s` | ❌ 否 → **被重設，且精度由 2 位變 5 位** |
| 管路坡度 | `0" / 12"`、`1/2" / 12"`（比值） | `0.00°`、`0.60°`、`1.19°`、`2.39°`（角度） | ❌ 否 → **被重設，且表示法由比值改為角度** |

底層 `raw` 值未變（`airDensity.raw` 前後均為 `0.034064786987`），僅顯示改變。**保留值的規律是「綁在單位上」**：換單位即套用新單位的預設精度，故 `cP`(2 位)→`Pa-s`(5 位) 精度改變，而 `SF` 與 `m²` 預設同為 0 位故面積精度未變——**這也意味著預設精度在英制時就已不足以支撐面積×係數的法規計算，並非切換造成**。

### L14: `get_mep_settings` 的坡度 `display` 欄不跟隨專案單位設定（2026-08-11 新增）

| 項目 | 詳細說明 |
|------|------|
| **限制** | `get_mep_settings` 回傳的 `Pipe.slopes[].display` 欄位**以角度格式化**，不反映專案的坡度顯示單位設定 |
| **實測** | Revit UI（`Manage → MEP Settings → Pipe Settings → Slopes`）已設為百分比並顯示 `0.0000%`、`1.0417%`、`2.0833%`、`4.1667%`；同一時點工具的 `display` 仍回 `0.00°`、`0.60°`、`1.19°`、`2.39°`。驗算 `atan(0.01041667) = 0.5968° ≈ 0.60°`，確認其以角度格式化 |
| **正確的欄位** | **`percent` 與 `ratio_1_in` 是對的**（`percent: 1.041667` 對應 UI 的 `1.0417%`、`ratio_1_in: 96` 對應 1:96） |
| **誤判風險** | 讀者會據 `display` 推論「本專案坡度以角度表示」，實際上是百分比。在單位盤點／凍結的情境下，這正是要防的那種誤讀 |
| **AI 應對策略** | 判讀坡度**一律採用 `percent` 或 `ratio_1_in`，忽略 `display`**。若需向使用者呈現坡度，自行由 `percent` 換算，並註明來源欄位 |
| **附帶問題** | 工具回傳的 `slopesNote` 寫著「display 會被專案的坡度顯示精度四捨五入」，暗示 display 跟隨專案設定——**該註記與實際行為不符**，應一併修正 |
| **未來方案** | 修正 `display` 的格式化改用 `SpecTypeId.Slope`（而非 Angle），或直接移除該欄位以免誤導 |

**lesson 起源**：2026-08-11 執行單位凍結時，比對 Revit UI 截圖與工具回傳值發現不一致。一支用來盤點單位的工具本身有單位回報缺陷——這也說明**任何工具的輸出都應與 UI 交叉驗證至少一次**，尤其在單位相關的工作上。

### L15: 連結模型的兩個陷阱——白名單品類、以及**單位跟著連結走**（2026-08-11 新增）

#### (a) `query_linked_elements` 是白名單，`query_elements_with_filter` 不是

兩支工具名稱相近、用途相近，**但品類解析機制完全不同**：

| 工具 | 品類解析 | 可查範圍 |
|---|---|---|
| `query_elements_with_filter`（主模型） | **名稱字串比對**，非白名單 | 幾乎任何品類，含 `Spaces`（見 L12） |
| `query_linked_elements`（連結模型） | **白名單** | 僅 MEP：`Pipes`／`Ducts`／`CableTrays`／`Conduits`／`PipeFittings`／`DuctFittings`；CSA：`Walls`／`Floors`／`StructuralFraming`／`StructuralColumns`／`Columns` |

實測：對連結模型查 `Roofs` 回傳明確錯誤 `無法辨識品類: Roofs`，並列出允許清單。**`Roofs`、`Rooms`、`Spaces`、`Doors`、`Windows` 等皆不可查。**

**AI 應對策略**：需要連結模型中白名單以外的品類時，**不要嘗試各種品類名稱拼法**（那會觸發「類別名稱窮舉式搜尋」的緊急停止模式）。改以下列擇一：(a) 用白名單內的相近品類推估（如以 `Floors` 推估樓板範圍）；(b) 請使用者改開該連結模型為主文件後再查；(c) 明確告知此為工具邊界。

#### (b) ⚠️ 連結元素的參數值以**連結模型自己的單位**回傳

**這是最容易造成無聲錯誤的一條。**

實測情境：主模型（MEP）已切為公制（`set_project_units mode=taiwan`），連結的建築模型仍為英制。查詢連結模型的 `Floors` 時，`Area` 回傳 **`4051 SF`**——**英制**，不是主模型的 m²。

**風險**：使用者剛完成單位凍結、確認主模型為公制，看到回傳值 `4051` 時極可能直接當成 m²。**實際差距為 10.764 倍。**

| 誤讀 | 實際 |
|---|---|
| 4,051 m² | **376.3 m²** |

**AI 應對策略**：

1. 讀取連結元素的任何**帶單位物理量**（面積、長度、體積、流量）時，**必須檢查回傳字串是否附單位符號**，不得僅取數值。
2. 主模型的單位設定**不適用於**連結模型的回傳值。**單位凍結只凍結主模型。**
3. 跨模型比對數值前，**一律先確認雙方單位**。這是 `domain/mep-space-demand-matrix.md` 第 5-1 節「單位凍結」未涵蓋的範圍——該節只處理主模型。

**lesson 起源**：2026-08-11 為 MEP 空間需求矩陣查詢建築模型屋頂範圍時發現。先撞到 (a) 白名單限制，改用 `Floors` 後撞到 (b) 單位陷阱。兩者皆為單次查詢即暴露，但若未留意回傳字串中的 `SF`，(b) 會產生一個看似合理、實際差 10 倍的建築面積。

---

### L16: `modify_element_parameter` 吃 **Revit 內部單位**，不吃專案顯示單位（2026-08-12 實測新增）⚠️

**分級：L2（工具行為與直覺相反，會靜默寫入錯誤資料）**

L15 是**讀**的單位陷阱，這條是**寫**的——嚴重得多，因為寫錯不會有任何錯誤訊息，資料就這樣進去了。

**實測（`WORK_M1_02_Space-Demand-Matrix`，Space `EXAM 3-4 1304`，專案 AirFlow 顯示單位已凍結為 m³/h）**：

| 寫入值 | 讀回值 | 比值 |
|---:|---:|---:|
| `89.2` | **9093** m³/h | ×101.94 |
| `0.875` | **89** m³/h | ✓ |

**101.94 = ft³/s → m³/h 的換算率**（1 ft³/s = 0.0283168 m³/s × 3600）。

也就是說：**要寫入 89.2 m³/h，參數要給 0.875。** 工具把值原樣送進 Revit API，而 Revit API 的 `Parameter.Set(double)` 一律是內部單位（英制系）——風量是 ft³/s，長度是 ft，面積是 ft²。**專案單位設定只影響顯示，不影響 API 寫入。**

**危險程度**：若批次寫入 29 筆 §102 法定風量而未換算，**29 筆會全部大 102 倍**，沒有任何警告。而明細表會照常顯示、照常有 Grand Total——與「計算欄位漏乘面積」是同一個病徵：**外觀完全正常，數值全錯**。

**AI 應對策略**：

1. **寫入任何帶單位的數值型參數之前，先寫一筆測試值再讀回核對。** 這是一次呼叫的成本，換掉整批資料靜默寫錯的風險。
2. 確認後，把換算率寫進當次作業紀錄，批次寫入時逐筆換算。
3. **不要假設「讀回來是 m³/h，寫進去也是 m³/h」。** 讀走顯示單位、寫走內部單位，兩條路徑不對稱。
4. 已驗證的只有 **AirFlow**。長度、面積、體積、溫度等**尚未驗證**，每一種第一次寫入前都要各自測一筆。

**lesson 起源**：2026-08-12 測試 `Air Changes per Hour` 是否由 `Specified Supply Airflow` 驅動時，順手撞到。原本的目的不是查單位——**這條 lesson 是測另一件事的副產品**，若當時只寫入不讀回，就會帶著 ×102 的錯誤往下走。**呼應 L15 的通則：帶單位的物理量，寫完一定要讀回。**

---

### L17: 批次寫入前先把作用視圖切離明細表（2026-08-12 實測新增）

**分級：L3（不是能力限制，是效能陷阱；症狀為逾時，容易被誤判為連線故障）**

**實測**：對 69 個 Space 批次寫入自建參數。作用視圖為一張 16 欄 × 71 列、含計算欄位的明細表時：

| 批次大小 | 結果 |
|---:|---|
| 17 | 15 成功、**2 逾時** |
| 13 | 7 成功、**6 逾時** |
| 7 | **7 筆全部逾時** |

此時**唯讀呼叫仍瞬間回應**（`get_active_view` 正常），因此不是連線故障。

**切換作用視圖至一般平面圖後，同樣的 14 筆一批全部秒過，其後 6 筆亦然。**

**成因**：Revit 對作用中的明細表在每次參數變更後重算。寫 69 個參數 = 重算 69 次整張表，且欄位越多、含計算欄位越多，單次重算越貴。負荷會累積，故批次越後面越容易逾時。

**AI 應對策略**：

1. **批次寫入 Space／Room 參數之前，先 `set_active_view` 切到一般平面圖**，寫完再切回。
2. **不要把「逾時」直接判為連線問題。** 先發一個唯讀呼叫（如 `get_active_view`）分辨：唯讀正常＋寫入逾時 = 前景視圖成本問題；兩者皆失敗 = 連線問題。
3. 縮小批次**不會**解決此問題（實測 7 筆一批反而全滅），**切視圖才會**。

**lesson 起源**：2026-08-12 為 69 個 Space 寫入判定狀態欄位時發生。當下第一反應是縮小批次，連續三次縮小都更糟；改以「唯讀是否仍正常」分辨後才定位到作用視圖。**縮小批次是直覺的處置，但方向錯了——症狀相同、成因不同的兩件事需要不同的分辨動作。**

---

### L18: Space 的內建外氣機制對 MCP 完全封閉，而且不該用它承載本地判斷（2026-08-12 實測新增）

**分級：L1（能力邊界）＋ 方法論警告**

#### 實測結果：四個環節逐一測過，沒有一條路通

| 參數 | 嘗試 | 結果 |
|---|---|---|
| `Space Type` | 寫入 | ❌ `不支援的參數類型: ElementId` |
| `Outdoor Air Method` | 寫入 | ❌ `參數 Outdoor Air Method 是唯讀的` |
| `Outdoor Air per Area` | 寫入 | ❌ `參數 Outdoor Air per Area 是唯讀的` |
| `Outdoor Airflow` | — | 衍生值，由上述三者決定 |

**整條鏈都要在 GUI 做。**

#### 為什麼 `Space Type` 是 ElementId 而不是文字

**Space 是 system family，但 `Space Type` 不是 family type。** 這是 Revit 命名造成的常見混淆——同一個物件上有三個不同的「type」：

| 名稱 | 屬於 |
|---|---|
| Family Type | 族群系統 |
| **`Space Type`** | **Energy Analysis 設定，非族群系統** |
| `Construction Type` | 同上，外殼熱性能 |

`Space Type` 的值**指向一個專案層級的設定元件**，該元件內含：每人外氣量、每面積外氣量、照明負載密度、電力負載密度、人員密度、使用排程。因此參數型別是 ElementId。

`<Building>` 表示尚未逐室指定，繼承專案層級的 Building Type 設定。

#### ⚠️ 更重要的是：就算能改，也不該把本地判斷放進去

內建 Space Type 的數值來自國際標準（ASHRAE 系）。直覺的處置是「自建一組本地版 Space Type」。**不建議。**

以台灣為例，一組 Space Type 要填的六項裡：

| 項目 | 本地有法定值嗎 |
|---|---|
| 每面積外氣量 | ✅ 建築設備編 §102 |
| 照明負載密度 | ⚠️ 用戶用電設備裝置規則 §36 有，但那是**電力計算**用途，非負荷分析 |
| 每人外氣量 | ❌ |
| 人員密度 | ❌ |
| 設備負載密度 | ❌ |
| 使用排程 | ❌ |

**六項只有一項查得到。其餘五項填進去，就變成藏在設定物件裡的假設。**

> **設定物件是黑盒。把判斷塞進去，就看不見它是判斷了。**
>
> 這與原則 4「決策者欄是資格檢查」互為反面——**能被稽核的前提是它在表面上**。
> 自建參數欄位（值 ＋ 狀態 ＋ 理由字串）逐室可見、可分組、可統計；Space Type 只看得到一個名字。

#### 應對策略：不要讓內建機制吐出本地答案，分開處理

| 用途 | 交給誰 |
|---|---|
| 外殼傳導、日射、內部發熱的**幾何計算** | Revit 負荷分析（它擅長這個） |
| **外氣負荷** | **自行計算**——Revit 會用它自己的內建外氣量 |
| 法定通風量檢核 | 自建欄位（見 `mep-space-demand-matrix.md` 5-4） |

**必須講明的代價**：在未調整內建設定的情況下，Revit 產出的 `Calculated Cooling Load` 是「**不含正確外氣負荷**」的版本，**不得直接當設計容量**，須另加外氣負荷。

在濕熱氣候區，外氣除濕負荷佔比很高，這一塊既不能省、也不能交給境外預設值。

**lesson 起源**：2026-08-12 準備跑 `Heating and Cooling Loads` 前檢查輸入時發現——某室內建 `Outdoor Airflow` 與同室依 §102 計算所得相差**約 10 倍**。單位（m³/h）與精度都正確，**錯的是數值背後的標準來源**。這正是本檔 L15／L16 之外的第三類單位相關陷阱：**不是單位錯，是值的出處錯**，而前兩者的檢查方法對它完全無效。

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
