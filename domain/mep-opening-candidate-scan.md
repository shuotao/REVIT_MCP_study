---
name: mep-opening-candidate-scan
description: "MEP 開孔候選掃描 SOP（scan_opening_candidates）：在 detect_clashes 幾何核心之上，唯讀推導管線穿越結構的開孔候選清單，含建議尺寸、candidate/review_required 狀態與 warningCodes。第一版邊界為掃描專用，不建套管、不建開孔族群、不放預覽標記。當使用者提到開孔候選、開孔預掃、opening candidate、預留孔洞、套管前置檢核、scan opening 時觸發。"
metadata:
  version: "0.1"
  updated: "2026-07-28"
  created: "2026-07-28"
  contributors:
    - "NicheSam (SC REVIT, 待確認真名)"
  references:
    - "Issue #99"
  related:
    - mep-csa-clash-detection.md
    - sleeve-classification-protocol.md
    - beam-penetration-base.md
  referenced_by: []
  tags: [開孔, opening, 開孔候選, MEP, 套管前置, clearanceMm, review_required, scan_opening_candidates]
---

# MEP 開孔候選掃描 SOP (scan_opening_candidates)

> **狀態：骨架文件（skeleton）。** 本檔由架構決策自動產出，工程細節（尺寸公式常數、狀態判定門檻、TS schema 缺口）標記 `TODO 待補`，須由 @NicheSam（SC REVIT）在下一輪月小聚補值後才可視為完整 SOP。補值前，任何 AI 代理不得憑空捏造門檻數字或直接對外承諾此工具已可用於自動建模。

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
  - mepElementId, hostElementId, linkInstanceId
  - entry (XYZ), exit (XYZ), center (XYZ)
  - suggestedOpeningSize: { 依品類而異，見〈開孔尺寸規則〉}
  - status: "candidate" | "review_required"
  - warningCodes: [...]                                        # 見〈狀態判定與 warningCodes〉
```

> `TODO 待補`：確切的回傳欄位型別（是否為 nested object、單位是否統一 mm、`suggestedOpeningSize` 的 schema）由 @NicheSam 依實作補齊。

## 開孔尺寸規則

| MEP 品類 | 建議開孔尺寸公式 |
|:---|:---|
| Pipe / Conduit | 直徑 + 雙側 `clearanceMm`（即 `建議直徑 = 管徑 + 2 × clearanceMm`） |
| Duct / CableTray | 寬 × 高，兩軸各自 + 雙側 `clearanceMm`（即 `建議寬 = 管寬 + 2 × clearanceMm`，`建議高 = 管高 + 2 × clearanceMm`） |

`TODO 待補`：矩形風管/電纜架斜向穿越時，是否需要依投影角度修正尺寸（而非直接用標稱寬高）——目前規則假設近似正交穿越，斜穿情形一律落入 `review_required`（見下）。

## 狀態判定與 warningCodes

`status` 只有兩種取值，語意固定：

* **`candidate`**：幾何條件單純（近正交穿越、尺寸合理、僅涉及單一結構元件），可進入下一步（人工確認或後續建套管流程）。
* **`review_required`**：以下任一條件觸發，必須人工複核，**不得自動晉升為 candidate**：
  * 穿越樑或柱（`StructuralFraming` / `StructuralColumns`）—— 結構風險較高，優先人工複核
  * 斜向穿越（穿透方向與結構主軸夾角超出 `TODO 待補` 門檻角度）
  * 建議開孔尺寸不足（小於 `TODO 待補` 最小可行開孔尺寸）
  * 交集長度過短（`TODO 待補` 門檻值以下的貫穿長度，可能是誤判碰撞而非真實開孔需求）

`warningCodes`（`TODO 待補` 確切代碼命名，暫列語意分類）：

| 語意分類 | 說明 |
|:---|:---|
| 穿樑柱 | 交集對象為 StructuralFraming / StructuralColumns |
| 斜穿 | 穿透方向非近正交 |
| 尺寸不足 | 建議開孔尺寸小於最小可行值 |
| 過短交集 | 貫穿長度過短，疑似誤判 |

## 設計原則

**「掃描成功 ≠ 全部可自動建立」。** 這是本 SOP 最重要的心理模型：`scan_opening_candidates` 回傳非空清單，不代表這些候選都能無腦轉成套管或開孔族群。

實測依據：13 筆掃描結果中，12 筆為穿樑（`review_required`），僅 1 筆為穿樓板（`candidate`）。換言之，**多數真實案例會落在 `review_required`**，AI 代理與下游流程都不應假設「有候選 = 可批次自動建模」，必須尊重狀態機的分流結果。

## 反模式示警

* **不得依賴 Revit Idling 事件隱藏互動狀態**：掃描過程中若需要向使用者展示進度或暫停等待輸入，一律透過既有 bridge 機制（`ExternalEventManager`）明確排隊執行，不可用 Idling handler 掩蓋 UI 執行緒的等待狀態——這會讓工具呼叫方誤判執行已完成。
* bridge 呼叫一律走 `ExternalEventManager`，禁止繞過既有 WebSocket/Revit 命令派發管道直接操作 Revit API（同 `CLAUDE.md` 的 Do Not Bypass MCP 規則）。
* 不得在 `clearanceMm` 缺值時靜默假設數值（見 Phase 2）。
* 不得將 `review_required` 候選在下游流程中直接當 `candidate` 處理。

## 已知缺口與後續流程

* **`detect_clashes.csaSource.linkInstanceId` 的 TS schema 未公開**：C# 端已能讀取 CSA 來源為連結模型時的 `linkInstanceId`，但目前 `MCP-Server/src/tools/clash-tools.ts` 的 `detect_clashes.csaSource` 輸入定義只有 `categories`（見〈參考 / Reference〉的驗證結果），未見 `linkInstanceId` 欄位。`scan_opening_candidates` 若要支援「CSA 亦為連結模型」的情境，需先由維護者補上這個 TS schema 欄位，本 SOP 暫不假設其存在。
* **候選清單的下游銜接**：`candidate` / `review_required` 清單產出後，銜接既有：
  * `domain/sleeve-classification-protocol.md`（套管身分判定：穿梁/穿牆/穿板分類邏輯，可用於候選清單的二次分類）
  * `domain/beam-penetration-base.md`（穿梁套管檢核基礎協議：`review_required` 中的穿樑柱候選進入正式檢核前應對照此協議的元素識別與樓層一致性規範）
* 建套管/開孔族群/預覽標記功能屬未來版本，本 SOP 不涵蓋，待第一版驗證穩定後另立新 domain 檔或於本檔新增章節（不得回頭修改〈第一版邊界〉既有承諾）。

## 參考 / Reference

* `detect_clashes`（`MCP-Server/src/tools/clash-tools.ts`）—— 本工具依賴的既有幾何核心，`mepSource` / `csaSource` 的輸入結構為本工具 `mepSource` / `structureSource` 的設計基礎。
* `get_connector_info`（`MCP-Server/src/tools/mep-tools.ts`）—— 可用於補充 MEP 元件的接頭座標與形狀資訊，輔助尺寸規則的邊界判斷（選用，非必要依賴）。
* `domain/mep-csa-clash-detection.md` —— 碰撞偵測流程 SOP，本檔 Phase 1 環境偵察與 Curve-to-Solid 架構說明直接沿用。
* `domain/sleeve-classification-protocol.md` —— 套管身分識別協議，候選清單下游銜接。
* `domain/beam-penetration-base.md` —— 梁穿孔檢核基礎協議，`review_required` 中穿樑柱候選的正式檢核依據。

> 註：`scan_opening_candidates` 本身尚未在 `MCP-Server/src/tools/*.ts` 中找到對應實作（已 grep 全目錄確認不存在）。本檔為該工具的**設計契約（SOP skeleton）**，供實作前對齊 I/O 與狀態機語意；工具落地後，需回頭在本節補上實際檔案位置與 `inputSchema` 對照，並移除本註記。
