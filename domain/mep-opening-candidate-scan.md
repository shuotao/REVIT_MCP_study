---
name: mep-opening-candidate-scan
description: "MEP 開孔候選掃描 SOP（scan_opening_candidates）：在 detect_clashes 幾何核心之上，唯讀推導管線穿越結構的開孔候選清單，含建議尺寸、candidate/review_required 狀態與 warningCodes。第一版邊界為掃描專用，不建套管、不建開孔族群、不放預覽標記。當使用者提到開孔候選、開孔預掃、opening candidate、預留孔洞、套管前置檢核、scan opening 時觸發。"
metadata:
  version: "1.1"
  updated: "2026-08-24"
  created: "2026-07-28"
  contributors:
    - "NicheSam (SC REVIT, 待確認真名)"
  references:
    - "Issue #99"
    - "Issue #99 comment 5128684040 (2026-07-30, @NicheSam)"
  related:
    - mep-csa-clash-detection.md
    - sleeve-classification-protocol.md
    - beam-penetration-base.md
  referenced_by: []
  tags: [開孔, opening, 開孔候選, MEP, 套管前置, clearanceMm, review_required, scan_opening_candidates]
---

# MEP 開孔候選掃描 SOP (scan_opening_candidates)

> **狀態：v1 已實作；Revit 2024 runtime smoke 由貢獻者 @NicheSam 於其環境完成回報（本 repo 未複驗）。** 尺寸公式常數、回傳 schema、狀態判定門檻等工程細節由 @NicheSam（SC REVIT）於 2026-07-30 在 Issue #99 留言（comment id `5128684040`）補值；MCP 工具定義位於 `MCP-Server/src/tools/clash-tools.ts`，Revit 端候選映射位於 `MCP/Core/OpeningCandidateScanner.cs`。此狀態不等於可自動建模或自動建套管。

## 目的與第一版邊界

**目的**：在不改動模型的前提下，掃描 MEP 管線（Pipe / Duct / CableTray / Conduit）與 CSA 結構體（樑 / 柱 / 板 / 牆）的幾何交集，推導「開孔候選清單」——每筆候選給出建議開孔尺寸與後續可否自動建立的狀態判定，供人工或後續流程決策是否放樣套管。

**第一版邊界（硬限制，不得擅自擴權）**：

* ✅ 只做唯讀掃描與尺寸/狀態計算。
* ❌ 不建立套管（Sleeve）元件。
* ❌ 不建立開孔族群（Opening Family / Generic Model void）。
* ❌ 不在視圖中放置任何預覽標記（marker / tag / 上色）。

任何 AI 代理讀到本檔後，若使用者要求「直接幫我開孔」，第一版應明確答覆：本工具目前只到候選清單為止，建套管/開孔族群屬於下一階段（見〈已知缺口與後續流程〉），需使用者另行確認或呼叫 `sleeve-classification-protocol.md` / `beam-penetration-base.md` 銜接的既有流程。

## 架構說明

`scan_opening_candidates` 建於既有 `detect_clashes` 幾何核心之上，**不重新實作第二套幾何引擎**：

```
detect_clashes 的 Curve-to-Solid 降維策略（見 domain/mep-csa-clash-detection.md）
  MEP 管線 → 抽取中心線 (1D Curve)
  CSA 結構 → 保持實體 (3D Solid)
  碰撞 = Curve 穿過 Solid → 取得穿透線段 (entry / exit / 貫穿長度)

scan_opening_candidates 在此之上疊加一層「開孔判斷層」：
  穿透線段 + MEP 元件尺寸參數 → 建議開孔尺寸（見〈開孔尺寸規則〉）
  穿透線段 + 結構品類/角度/尺寸 → candidate / review_required 狀態（見〈狀態判定與 warningCodes〉）
```

沿用 `detect_clashes` 的原因：碰撞幾何運算（Curve-to-Solid、Transform 校正、BoundingBox 粗篩）已驗證可用，重寫等於引入第二套未驗證的幾何邏輯，違反「同一問題不做兩套引擎」原則。

## Phase 1: 環境偵察

同 `domain/mep-csa-clash-detection.md` Phase 1，先確認來源：

```
Tool: get_linked_models
目的: 找到 MEP 連結模型的 LinkInstanceId（若 MEP 為連結模型而非主模型）

Tool: query_linked_elements
目的: 確認 MEP 品類與參數可讀（Pipes / Ducts / CableTrays / Conduit）

Tool: get_active_schema（或等效）
目的: 確認 CSA 結構品類（Walls / Floors / StructuralFraming / StructuralColumns）數量
```

**範圍預設行為**（補值來源：Issue #99 comment 5128684040，2026-07-30）：使用者未指定掃描範圍時，Agent 應先用 `get_linked_models` 盤點主模型與**全部已載入的 Revit Link**，再以明確的來源 ID（主模型 or 各 Link 的 `linkInstanceId`）逐組掃描；使用者指定特定系統或特定 Link 時才縮小範圍。不得省略盤點步驟直接假設「只掃主模型」或「只掃第一個 Link」。

## Phase 2: 掃描參數界定

> AI 向使用者確認以下參數，全部須為**明確值**，不得使用預設猜測值靜默代入：

| 參數 | 必填 | 說明 |
|:---|:---:|:---|
| `mepSource` | 是 | MEP 來源：主模型或連結模型（含 `linkInstanceId`、`categories`、`filters`，語意同 `detect_clashes.mepSource`） |
| `structureSource` | 是 | 結構來源：主模型或連結模型（語意同 `detect_clashes.csaSource`） |
| `clearanceMm` | **是（無預設值）** | 開孔尺寸的雙側預留量（mm）。**必須由使用者給出明確數字**，不可由 AI 代入業界慣例值靜默計算——不同專案的套管規範差異大，靜默假設會產生錯誤尺寸的開孔候選 |
| `levels` | 選填 | 樓層範圍過濾 |
| `categories` | 選填 | 品類子集過濾（否則沿用 mepSource/structureSource 的預設清單） |
| `maxCount` | 選填 | 最大回傳候選數（防止超大模型一次回傳過量結果） |

**鐵則**：`clearanceMm` 未提供時，工具應回傳明確錯誤或要求補值，**不得**以 0 或任意常數靜默執行——這會讓後續尺寸規則產生假的「合理」候選。

## Phase 3: 執行掃描

```
Tool: scan_opening_candidates
參數:
  mepSource: { linkInstanceId?, categories, filters? }       # 同 detect_clashes.mepSource
  structureSource: { categories }                             # 同 detect_clashes.csaSource
  clearanceMm: <明確數值，必填>
  levels: [...]                                                # 選填
  categories: [...]                                            # 選填
  maxCount: <N>                                                # 選填

回傳: 候選清單，每筆含
  - candidateId                                                 # 候選編號，例 "OC-001"
  - revitLookup: { penetratingElement, hostElement }             # 詳見下方 schema
  - entry (XYZ), exit (XYZ), center (XYZ)                        # project coordinates，沿用 detect_clashes 既有座標
  - suggestedOpeningSize: { 依品類而異，見〈開孔尺寸規則〉}
  - openingBottom: { basis, projectLevelName, projectElevationMm, offsetFromLevelMm }
  - status: "candidate" | "review_required"
  - warningCodes: [...]                                        # 見〈狀態判定與 warningCodes〉
```

**部分候選失敗的處理**（補值來源：Issue #99 comment 5128684040）：單筆候選讀取失敗（例如幾何解析錯誤、Link 已卸載）不得中止整批掃描，應保留其他成功候選；失敗筆數除回傳技術代碼外，也要附上人能理解的原因與建議處理方式（例如「Link 未載入，請於 Revit 開啟後重新掃描」），不得只回傳空陣列或籠統錯誤。

### 回傳欄位 Schema（補值：@NicheSam，Issue #99 comment 5128684040，2026-07-30）

`suggestedOpeningSize`、`openingBottom`、`revitLookup` 皆為 **nested object**；所有對外公開的長度單位統一為 **mm**。

* `suggestedOpeningSize`：
  * `shape`："round" | "rectangular"
  * `unit`：固定 `"mm"`
  * 圓形用 `diameterMm`；矩形用 `widthMm` / `heightMm`（不適用者填 `null`，不省略欄位）
  * `clearanceMmPerSide`：本筆候選實際採用的雙側預留量（見〈開孔尺寸規則〉）
* `openingBottom`（開孔下緣，以**主模型樓層**為基準，**不以開孔中心**作為標高基準）：
  * `basis`：固定 `"opening_bottom_edge"`
  * `projectLevelName`：對應的主模型樓層名稱
  * `projectElevationMm`：開孔下緣在專案座標系的標高（mm）
  * `offsetFromLevelMm`：開孔下緣相對該樓層的偏移量（mm）
  * 若必要尺寸或開孔下緣無法可靠取得，對應欄位保持空值並在候選的失敗原因中說明，**不得**用 BoundingBox 或同名樓層猜測代入（觸發〈狀態判定與 warningCodes〉的 `opening_bottom_unresolved`）
* `revitLookup`：每筆候選回傳穿管物件（`penetratingElement`）的 `elementId`；若物件位於 Link，需**同時**回傳 `linkInstanceId` 與 `linkedElementId`（不可只回傳其中一個）。`hostElement` 比照同一結構（`documentKind`: `"main"` | `"link"`）。

回傳格式範例（Pipe 穿連結樓板，出處：Issue #99 comment 5128684040）：

```json
{
  "candidateId": "OC-001",
  "revitLookup": {
    "penetratingElement": {
      "documentKind": "main",
      "elementId": 13723914
    },
    "hostElement": {
      "documentKind": "link",
      "linkInstanceId": 13289632,
      "linkedElementId": 199201
    }
  },
  "suggestedOpeningSize": {
    "shape": "round",
    "unit": "mm",
    "diameterMm": 200.0,
    "widthMm": null,
    "heightMm": null,
    "clearanceMmPerSide": 50.0
  },
  "openingBottom": {
    "basis": "opening_bottom_edge",
    "projectLevelName": "B1FL",
    "projectElevationMm": -4859.0,
    "offsetFromLevelMm": 941.0
  }
}
```

## 開孔尺寸規則

`clearanceMm` 定義為開孔**每一側**的預留量（單位 mm，MCP 工具合約中維持必填、不給預設值——見 Phase 2）。

| MEP 品類 | 建議開孔尺寸公式 |
|:---|:---|
| Pipe / Conduit | `建議直徑 = 標稱直徑 + 2 × clearanceMm` |
| Duct / CableTray | `建議寬度 = 標稱寬度 + 2 × clearanceMm`，`建議高度 = 標稱高度 + 2 × clearanceMm` |

使用者未指定 `clearanceMm` 時，Agent 工作流程應先明確告知即將採用的數值（例如「本次採用每側 50 mm」）並取得使用者確認後才代入 `clearanceMm`；使用者指定其他數值（例如 100 mm）時，一律以使用者數值覆寫，不得沿用先前對話或其他專案的預設值。

**（補值：@NicheSam，Issue #99 comment 5128684040）v1 不計算斜穿造成的投影放大。** 只要判定為斜向穿越，就保留「標稱尺寸 + 預留量」的量測結果作為 `suggestedOpeningSize`，同時觸發 `review_required` / `oblique_penetration`（見〈狀態判定與 warningCodes〉）。該尺寸僅供人工複核參考，**不得**視為可直接施工的最終孔徑。

## 狀態判定與 warningCodes

`status` 只有兩種取值，語意固定：

* **`candidate`**：幾何條件單純（近正交穿越、尺寸資料齊全、僅涉及單一結構元件、開孔下緣可解析），可進入下一步（人工確認或後續建套管流程）。
* **`review_required`**：以下 7 條門檻任一觸發，必須人工複核，**不得自動晉升為 candidate**（補值來源：Issue #99 comment 5128684040，2026-07-30）：

| # | 觸發條件 | `warningCodes` |
|:---:|:---|:---|
| 1 | 穿越樑（`StructuralFraming`） | `structural_framing_review` |
| 2 | 穿越柱（`StructuralColumns`） | `structural_column_review` |
| 3 | 任何可量測的非正交穿越 | `oblique_penetration` |
| 4 | 無法可靠取得 Host 表面法向 | `host_normal_unresolved` |
| 5 | 實際交集長度 `< 10 mm`（剛好 10 mm 不觸發） | `short_intersection` |
| 6 | 無法可靠取得必要標稱尺寸 | `size_data_missing` |
| 7 | 無法可靠計算開孔下緣或對應主模型樓層 | `opening_bottom_unresolved` |

**斜穿角度不設可自動放行的門檻值**（例如 5°、15° 等固定角度皆不採用）。原則是只要不是正交穿越，一律交由人工複核；數值計算只保留排除浮點誤差用的極小容差，並回傳實際量測到的偏差角度供人判讀，不做「小角度自動放行」的判斷。

**短交集門檻**採 `< 10 mm`；此數值來自 SC REVIT 現有的 1 cm 判定，用來排除擦邊與 Revit 幾何雜訊，**不宣稱為工程法規值**。

**v1 不設固定的「最小可行開孔尺寸」常數。** 理由：這類數值受結構技師要求、套管／防火填塞系統、材料、施工方式與專案標準影響，沒有一個放諸四海皆準的通用值。必要標稱尺寸無法可靠取得時，一律回傳 `size_data_missing` 並說明缺少什麼欄位，**不得**由 Agent 補上一個看似合理的數字（呼應 `CLAUDE.md` 的 Tool Call Data Honesty 原則）。

## 設計原則

**「掃描成功 ≠ 全部可自動建立」。** 這是本 SOP 最重要的心理模型：`scan_opening_candidates` 回傳非空清單，不代表這些候選都能無腦轉成套管或開孔族群。

實測依據：13 筆掃描結果中，12 筆為穿樑（`review_required`），僅 1 筆為穿樓板（`candidate`）。換言之，**多數真實案例會落在 `review_required`**，AI 代理與下游流程都不應假設「有候選 = 可批次自動建模」，必須尊重狀態機的分流結果。

## 反模式示警

* **不得依賴 Revit Idling 事件隱藏互動狀態**：掃描過程中若需要向使用者展示進度或暫停等待輸入，一律透過既有 bridge 機制（`ExternalEventManager`）明確排隊執行，不可用 Idling handler 掩蓋 UI 執行緒的等待狀態——這會讓工具呼叫方誤判執行已完成。
* bridge 呼叫一律走 `ExternalEventManager`，禁止繞過既有 WebSocket/Revit 命令派發管道直接操作 Revit API（同 `CLAUDE.md` 的 Do Not Bypass MCP 規則）。
* 不得在 `clearanceMm` 缺值時靜默假設數值（見 Phase 2）。
* 不得將 `review_required` 候選在下游流程中直接當 `candidate` 處理。

## 已知缺口與後續流程

* ~~`detect_clashes.csaSource.linkInstanceId` 的 TS schema 未公開~~ **（已解決，2026-08-10）**：`MCP-Server/src/tools/clash-tools.ts` 的 `detect_clashes.csaSource` 已補上 `linkInstanceId` 欄位（語意同 `mepSource.linkInstanceId`：為 0 或省略時從主模型讀取），對齊 C# 端 `MCP/Core/ClashDetector.cs` 的 `CollectCsaElements`（`linkInstanceId` 為 0 或缺省時走 `else` 分支查主模型 `doc`，非 0 時查對應 Link）既有行為。`scan_opening_candidates` 的 `structureSource` 可比照沿用同一欄位語意，不再是本 SOP 的已知缺口。
* **候選清單的下游銜接**：`candidate` / `review_required` 清單產出後，銜接既有：
  * `domain/sleeve-classification-protocol.md`（套管身分判定：穿梁/穿牆/穿板分類邏輯，可用於候選清單的二次分類）
  * `domain/beam-penetration-base.md`（穿梁套管檢核基礎協議：`review_required` 中的穿樑柱候選進入正式檢核前應對照此協議的元素識別與樓層一致性規範）
* 建套管/開孔族群/預覽標記功能屬未來版本，本 SOP 不涵蓋，待第一版驗證穩定後另立新 domain 檔或於本檔新增章節（不得回頭修改〈第一版邊界〉既有承諾）。

## 後續評估（不在 v1 實作承諾範圍）

以下兩項為 Issue #99 討論中的升級構想，@NicheSam 已在 comment 5128684040（2026-07-30）明確聲明**尚待評估、不納入本次 `scan_opening_candidates` 的實作承諾**。列於此處僅作紀錄，不得作為本 SOP 已核准的路線圖。

**構想一：由候選定位到 Revit 3D 畫面**

第一版只回傳候選編號、主模型／Link identity、ElementId、中心點與判斷原因，掃描時不自動逐筆切換畫面或建立預覽標記。後續若要評估「使用者指定查看某候選 → MCP Client／Agent 呼叫定位工具 → Revit 切換到局部 3D 檢查視圖並縮放」的流程，仍需先評估：主模型與 Link 的定位差異、座標轉換、固定 3D 檢查視圖的建立與復原、Section Box 範圍、視圖修改的復原邊界，以及 MCP Client 是否支援真正可點擊的操作元件。

**構想二：串接台灣 AEC 產業知識庫（HJPLUS Taiwan Architect Knowledge Base）**

構想是把候選的 Host 類型、材料、管線尺寸、開孔位置與專案條件交由獨立知識判斷流程查詢適用規範，回傳來源、版本、適用條件、衝突與需人工確認的原因。目前 [HJPLUS Taiwan Architect Knowledge Base](https://github.com/h30190/HJPLUS_Taiwan_Architect_KB) 尚無一套可直接套用到所有專案的「最小開孔尺寸」規則，且此類規則需要建築、結構、機電、防火填塞與施工等業內人員共同整理查證，非單一開發者可獨立完成。若後續推進，方向應是：Revit MCP 掃描核心只回報模型幾何事實、不依賴外部知識庫才能運作；知識判斷層提供有來源的建議、不直接核准或建立開孔；找不到規則、資料過期、規則衝突或涉及專業簽認時一律交回人工審查。

## 參考 / Reference

* `detect_clashes`（`MCP-Server/src/tools/clash-tools.ts`）—— 本工具依賴的既有幾何核心，`mepSource` / `csaSource` 的輸入結構為本工具 `mepSource` / `structureSource` 的設計基礎。
* `get_connector_info`（`MCP-Server/src/tools/mep-tools.ts`）—— 可用於補充 MEP 元件的接頭座標與形狀資訊，輔助尺寸規則的邊界判斷（選用，非必要依賴）。
* `domain/mep-csa-clash-detection.md` —— 碰撞偵測流程 SOP，本檔 Phase 1 環境偵察與 Curve-to-Solid 架構說明直接沿用。
* `domain/sleeve-classification-protocol.md` —— 套管身分識別協議，候選清單下游銜接。
* `domain/beam-penetration-base.md` —— 梁穿孔檢核基礎協議，`review_required` 中穿樑柱候選的正式檢核依據。

> 實作對照：公開 `inputSchema` 與唯讀標註在 `MCP-Server/src/tools/clash-tools.ts`；命令由 `MCP/Core/CommandExecutor.cs` 派發至 `MCP/Core/OpeningCandidateScanner.cs`，後者呼叫既有 `ClashDetector.DetectClashes`，不維護第二套碰撞演算法。2026-08-24 由 @NicheSam 回報的 Revit 2024 runtime smoke（模型檔名依本 repo 去識別化政策不記錄，詳見 `domain/anti-lessons.md` 第 56 條）：MEP 主模型、結構 link `13291389`，`clearanceMm=25`、`maxCount=10`，回傳 `totalCandidates=10`、`candidateCount=10`、`reviewRequiredCount=0`、`failedCount=0`，且缺少 `clearanceMm` 時正確拒絕。
