---
name: daylight-area-check
description: "居室採光／自然通風檢討的資料契約、逐室判讀與法規核驗 SOP；使用於 Revit 或其他 BIM adapter 尚未具備完整採光計算時。"
metadata:
  version: "1.1"
  updated: "2026-10-08"
  created: "2026-02-24"
  contributors:
    - "DAVID\\david"
    - "shuotao"
  references:
    - "建築技術規則建築設計施工編第41條（本 SOP 的條文、適用範圍與現行版本仍須以官方原始法源及專業人員核驗）"
    - "BIM_MCP @ 2faacc5: MCP/Core/CommandExecutor.cs:1767-1993; MCP-Server/src/tools/room-tools.ts:350-358"
    - "Archwiz archicad28-mcp @ 2e7184e（私有 APX/CEF source，僅作資料取得與不確定性設計參考）"
  related:
    - smoke-exhaust-review.md
    - references/building-code-tw.md
  referenced_by:
    - building-compliance
  tags: [採光, 自然通風, 法規, 居室, 開口, 資料契約, pending, daylight]
---

# 居室採光面積檢討流程

## 目前能力與法規邊界（先讀）

本檔同時保留歷史採光 SOP 與面積雙值警告，並新增跨 BIM adapter 的資料契約。它不是
「目前 MCP 已完成採光合規判定」的宣告，也不是法源本身。所有比率、係數、用途分類、
豁免與採用哪一個房間面積，都必須以適用地區、專案條件、官方原始法源及合格專業人員
核驗；未核驗時結果只能是 `pending`／設計檢討資料，不能輸出合規結論。

目前公開 Revit MCP 的 `get_room_daylight_info` 只取得房間與牆上 Door／Window 的基本資料。
`MCP/Core/CommandExecutor.cs`（commit `2faacc5`，`GetRoomDaylightInfo` 自第 1767 行）以 Room
boundary 找牆、`FindInserts` 找門窗，優先 `ToRoom`、其次 `FromRoom`，最後才以牆線投影 fallback
歸屬；外牆以 `WallFunction.Exterior` 或另一側無 Room 推定。它將內部 feet 換成 mm、輸出
round 後的寬／高／窗臺／窗頂，缺值會被補為 0，窗頂也可能由 `sill + height` 推得。
公開 tool surface 位於 `MCP-Server/src/tools/room-tools.ts:350-358`。它**沒有**計算 `Ae`、
採光比、玻璃面積、可開面積或 pass/fail，且尚缺 phase-aware 歸屬、glass／operable 證據、
curtain wall panel、skylight、完成面基準與遮蔽資料。不要把 tool description 當成實際 payload。

`check_smoke_exhaust_windows` 是另一條排煙審查流程，不是一般自然通風資料來源或替代公式；
它預設會上色（`colorize: true`），且使用天花下有效帶邏輯。參見
`MCP-Server/src/tools/smoke-exhaust-tools.ts:11-23` 與 `smoke-exhaust-review.md`。

## 資料契約：先取得、再判讀

### 1. 不混合 implementation knowledge 與法規結論

下列 Archwiz 觀察來自 pinned private source checkout `archicad28-mcp @ 2e7184e`，只用來設計可攜的
versioned schema 與不確定性處理；它不是公開 MCP 契約，也不把 APX 程式預設升格為法規。

|來源層|已觀察到的資料|不可直接推論的事|
|---|---|---|
|Archwiz private CEF/APX|native AreaLCF、Door／Window、3D geometry 形成 snapshot|公開 Archicad MCP 已有同一工具或欄位|
|Revit public MCP|Room、基本 opening 尺寸與推定外牆／歸屬|有效採光面積、玻璃／可開比例、合規結果|
|規則引擎（建議）|versioned schema、演算法版本、每欄 provenance／unit／status|未經法源核驗的數字可作合規判定|

每次讀取至少保存 `schemaVersion`、`algorithmVersion`、`capturedAt`、來源 revision，以及每個欄位的
`value`、`sourceUnit`、`provenance`（`observed|inferred|defaulted|manual|missing`）、
資料狀態 `status`（`known|pending|invalid|stale`）與獨立的 `confirmation`
（`confirmed|unconfirmed`）。若 `provenance=defaulted`，還要保存
`defaultApplied`（policy／algorithm version、reason、applied-at）；它使程式預設能與觀測值、
幾何推論及人工輸入區分。未確認的 default 必須令其影響的 axis 為 `pending`。長度和面積必須
明示原始單位與轉換；不得以顯示取位值取代計算原值，也不得以 0 代表 unknown。

最小讀取契約可分為：

- room：穩定 ID、樓層／phase、用途、原始面積、資料狀態與確認狀態；
- opening：穩定 ID、host、room-association evidence、外牆判定及來源、寬高、窗臺／窗頂及其相對基準；
- segment：可重算的 polygon／area、`lightFraction`、型式、`openRatio`、幾何與人工覆寫的
  provenance、default record 與 confirmation；
- evidence：snapshot／設定 revision、warnings、errors 與人工確認紀錄。

### 2. 可移植的 acquisition 與不確定性規則

Archwiz 的 native path 實際讀的是八種 AreaLCF 物件，不是 Archicad Zone；同名 room piece 還會依
story、unit、room key、接觸／重疊及牆分隔做群組。對 opening，它從 owner wall 中心兩側取樣並找
同層 AreaLCF，才產生 `roomSuggestion`；這是有容差的雙側幾何啟發式，不是原生 room relation 或
外牆量測。hotlink／sourceFloor 等情況要保留為 unavailable，而非假設為外窗。

該 path 以 native Door／Window 基本欄位與 home-story-relative sill 為起點，再將 transformed 3D
faces 投影；材質透明度只被二元分類。raster 以 opaque、glass、void 分類並以中心線切出 segment：
只有含有 transmitting glass，或在 `empty-opening` 時被允許的 void 的 segment，才得到
`lightFraction=1`；其餘為 0。這不是玻璃面積比例或光學透射率。型式與可開比也可能由有限 family
參數或幾何推定，應標成 inferred。

Archwiz 0.81 的預設值有三條不同路徑。acquisition 會把 `exposure=manual`、eligible 與其來源、
`opening-side` 與其來源、可讀取的 sill、suggested room ID、balcony，以及適用時的 `reviewScope`
寫到 opening top level，並只把這些實際套用的欄位名稱列入 `defaultsApplied`
（`AW:addon/archwiz_platform/Src/DaylightReview.cpp:224-237`）；`roomSuggestion` 本身仍是證據，
`defaultsApplied` 不包含 `openRatio`。其後的 geometry／type mapping 直接在 snapshot segment 寫入
部分比例，例如 fixed 為 0、door leaf 或 empty opening 為 1（
`AW:addon/archwiz_platform/Src/DaylightReview.cpp:271-313`）。最後在 evaluation 時，仍未設定比例的
非 fixed／非 unknown segment 才由 type-ratio 設定補值；無有效設定時 fallback 為 0.5（
`AW:addon/archwiz_platform/RFIX/DaylightCore.js:164-175`；
`AW:addon/archwiz_platform/Src/DaylightCalculation.hpp:142-147`）。

上述是當前 APX 的三種來源階段，不能誤稱為同一個 `defaultsApplied` 機制。BIM_MCP 的**未來**
portable policy 可把任何 policy-supplied value 正規化為 `provenance=defaulted`，另存
policy/version/reason、`defaultApplied` 與 `confirmation=unconfirmed`；這是建議契約，不是私有來源
目前已記錄的欄位。未確認 default 必須使受影響 axis 為 `pending`。幾何讀取失敗時，
如有先前已驗證的 adapter 或 manual segment 可作 fallback，必須連同 `geometryStatus` 和 fallback
evidence 輸出；沒有有效 fallback 時才阻擋該軸，絕不可靜默補零。

### 3. 逐室、逐軸的確定性判讀

在資料與法源均已確認時，演算法可保存下列可重算形式（其中門檻與係數由適用法源／專業設定提供，
不是本檔新增的法規常數）：

```text
daylight(room) = Σ_opening (
  [Σ_segment (segmentAreaAboveSillThreshold × lightFraction)]
  × confirmedEligibility_opening
  × confirmedBalcony_opening
  × confirmedSkylight_opening
)
ventilation(room) = Σ_opening Σ_segment (segmentArea × openRatio)
```

- 每個 opening 的 `confirmedEligibility`、`confirmedBalcony`、`confirmedSkylight` 都是無因次、
  opening-scoped、versioned 的 daylight-only 欄位，必須各自有 evidence 與獨立 confirmation，
  再套用到該 opening 的 segment 小計。天窗與遮蔽等組合若沒有已核驗規則，受影響 opening 的
  evidence 及其所屬 room daylight axis 都是 `pending`，不可盲目相乘；這些欄位不得套用到 ventilation。
- ventilation baseline 使用完整 segment area 與 `openRatio`，不套採光窗臺裁切、eligibility、陽台或
  天窗係數。未來若有通風專用 modifier，必須另有名稱、法源、version、evidence 與 confirmation，
  預設為 1，且不可重用 daylight modifiers。
- 資料／evidence 狀態與判讀結果是兩個 state machine：欄位用
  `known|pending|invalid|stale`；逐軸 outcome 用 `pending|pass|fail|not-applicable|exempt`，附 reasons
  與 evidence。`known` 是可用資料或累加量，不是 outcome。
- 採光與自然通風分開計算；unknown type 或 `openRatio` 只阻擋通風軸，不能單獨把已有有效透光幾何的
  採光軸改為失敗。
- 每一 room 各自與其適用 requirement 比較；其他房間的餘量不可補足該 room。不完整資料、無效
  polygon、overlap／coverage gap、未確認完成面或未確認法源，優先輸出 `pending`。
- 固定扇、可開型式、陽台／遮蔽、天窗或設備替代都要有欄位層級 evidence。設備替代需逐室 reason
  與 confirmation，並只改變該 room 的 ventilation requirement／outcome；不得用它縮放 opening
  ventilation area，否則不得聲稱符合自然通風規定。`exempt` 與 `not-applicable` 也必須保留逐室證據。

這些規則的目標是讓任何 adapter 的同一 snapshot 可以重跑與稽核，而不是把某一個 BIM 程式的
heuristic 或預設當作普遍事實。

### 4. 來源定位（實作證據，不是法源）

本節以 `BIM` 表示本 repository 的 commit `2faacc5`，以 `AW` 表示 Archwiz checkout 的 commit
`2e7184e`；以下均為 repository-relative path。它們記錄資料取得與程式行為，不能取代官方法規來源。

- `BIM:MCP/Core/CommandExecutor.cs:1767-1993`：目前 Revit room／opening payload、歸屬、單位轉換與
  fallback；`BIM:MCP-Server/src/tools/room-tools.ts:350-358`：公開 tool schema。
- `AW:addon/archwiz_platform/Src/HelpCenterPalette.cpp:660-681`：CEF private binding；
  `AW:addon/archwiz_platform/Src/AreaObjectReader.cpp:167-229`：AreaLCF reader。
- `AW:addon/archwiz_platform/Src/DaylightOpeningGeometry.hpp:102-239` 與
  `AW:addon/archwiz_platform/Src/DaylightGeometryRaster.hpp:33-158`：quantity、3D face、raster 與分區。
- `AW:addon/archwiz_platform/Src/DaylightRoomGroups.hpp:10-108` 與
  `AW:addon/archwiz_platform/Src/DaylightRoomSuggestion.hpp:34-68`：群組與雙側建議；
  `AW:addon/archwiz_platform/Src/DaylightReview.cpp:224-237,271-313`：acquisition defaults、
  `defaultsApplied` 與 geometry/type segment 比例。
- `AW:addon/archwiz_platform/RFIX/DaylightCore.js:1-90,164-175,203-210,218-226` 與
  `AW:addon/archwiz_platform/Src/DaylightCalculation.hpp:142-147,171-175,182-183`：prototype 的用途表、
  evaluation-time ratio fallback、逐 opening 採光修正與 room 累加；這是程式觀察，不是可直接採用的
  法規需求表。

## 前置條件
- Revit 模型已開啟
- 房間（Rooms）已正確建立並命名（需能識別用途，如：臥室、起居室、教室）
- 窗戶（Windows）或帷幕牆（Curtain Walls）已建立
- 樓板（Floors）已建立（用於計算樓地板面積）

## 執行步驟

### 步驟 1：研讀法規標準 (Analyze Regulation - Article 41；歷史 SOP，待核驗)

以下歷史整理供提出核驗問題與設定資料欄位使用；**不得在未查核官方現行原文、適用地區與專案條件前，
視為可執行的合規門檻。**

| 如果居室用途是... | 採光面積不得小於樓地板面積之... | 備註 |
| :--- | :---: | :--- |
| **學校教室**、幼兒園 | **1/5 (20.0%)** | |
| **住宅居室** (臥室/起居/客廳) | **1/8 (12.5%)** | 含寄宿舍臥室、醫院病房、托育機構 |
| **其他居室** | **1/8 (12.5%)** | 一般標準 |

**有效採光面積計算規則**：
1.  **高度限制**：地板面以上 **75cm 範圍內** 之窗或開口面積 **不計入**。
2.  **天窗**：有效採光面積按其採光面積之 **3倍** 計算。（⚠ 出處待確認：§41 原文未明確列出 3 倍係數，可能來自地方自治條例或實務慣例）
3.  **陽台/外廊遮蔽**：若開口外側設有寬度超過 **2m** 之陽台/外廊，有效採光面積按其採光面積 **70%** (0.7) 計算。（⚠ 出處待確認：§41 原文未明確列出 70% 折減，可能來自地方自治條例或設計手冊）
4.  **商業區**：水平間距達 **5m** 以上者，得免增加採光面積（需確認專案是否適用）。
5.  **住宅區深進**：建築深度超過 **10m**，各層背面/側面開口應在有效採光範圍內。

### 步驟 2：取得房間資訊 (Get Room Info)

```
使用工具：get_all_rooms 或 query_elements (Category: Rooms)
參數：
  - filters: 篩選名稱包含 "臥", "起居", "客廳", "教室", "病房" 等關鍵字
目的：取得所有居室及其 Area (樓地板面積)
```

### 步驟 3：搜尋關聯開口 (Find Associated Openings)

```
使用工具：spatial_query 或 geometry_analysis
邏輯：
  1. 找出圍繞該房間的牆（Room Bounding Walls）
  2. 找出這些牆上的窗（Windows）、門（Doors, 含玻璃部分）
  3. 必須確認開口是「對外」的 (Exterior)
```

### 步驟 4：計算有效採光面積 (Calculate Effective Daylight Area)

針對每個開口，計算其有效面積 `Ae`：

1.  **取得幾何/尺寸**：
    -   `Raw Area` = `Width` * `Height` (或從幾何提取玻璃面)
    -   **扣除 75cm 以下部分**：
        -   若 `Sill Height` (底高) >= 75cm -> `Area` = `Raw Area`
        -   若 `Sill Height` + `Height` < 75cm -> `Area` = 0
        -   若 `Sill Height` < 75cm 且 `Head Height` > 75cm -> 
            `Effective Height` = `Head Height` - 75cm
            `Area` = `Width` * `Effective Height`

2.  **應用係數**：
    -   `Skylight Factor`: 若為天窗 -> * 3.0
    -   `Balcony Factor`: 若有深陽台 (>2m) -> * 0.7
    -   `Ae` = `Area` * `Factors`

### 步驟 5：比對與判定 (Compliance Check)

計算 **採光比 (Daylight Ratio)** = `Σ(Ae) / Room Area`

**判定邏輯**：
-   若 居室類型 == 學校/幼兒園：
    -   `Result` = (Ratio >= 0.20) ? "合格" : "不合格"
-   若 居室類型 == 住宅/其他：
    -   `Result` = (Ratio >= 0.125) ? "合格" : "不合格"

### 步驟 6：結果輸出與標示 (Output & Visualization)

```
使用工具：override_element_color (Visual Indicator)
  - 合格：綠色 (Green)
  - 不合格：紅色 (Red)
```

產生 **採光檢討報告 (Daylight Checklist)**：
-   列出每個房間的 ID, Name, Area, Req. Area, Actual Daylight Area, Result.

## 範例對話（假設情境）

**用戶**：幫我檢查 2F 的採光是否符合法規

**AI**：好的；我會先取得房間、開口、phase、完成面和玻璃／可開證據，並核對適用法源。缺少任一
必要資料時會標為 pending，而不宣告合格或上色。
1.  篩選 2F 的居室空間...
2.  計算各房間有效開口面積（扣除台度 75cm 以下部分）...
3.  比對法規標準 (1/8 或 1/5)...

（執行後）

**AI**：這是示意，不是實際執行結果：已輸出逐室資料來源、未確認項與可重算式；待法源、完成面、
玻璃及可開資料確認後，才可依已核定規則產生結果或要求視覺化。

## 2026-05-22 補：面積雙值警告（L-029）

日本 BIM 模板（及部分台灣模板）的 Room 元素**同時保存兩個面積值**：

| 欄位 | 來源 | 用途 |
|------|------|------|
| `面積`（Area） | Revit 從牆邊界自動計算（幾何面積） | `get_rooms_by_level` / `get_room_daylight_info` 回的是這個 |
| `面積 部屋 調整値` | 建模者手填校正值 | 仕上表 / 法定報告 / 確認申請 |

**5/22 dry-run 實測**（日本 sample 1FL）：
- 店舗 26：面積 163.83 m² vs 部屋調整値 164.83 m²（差 1.00 m²）
- 駐車場 27：207.80 vs 208.14（差 0.34 m²）
- 廊下 2：21.50 vs 22.04（差 0.54 m²）

**對採光比的影響**：採光比 = Σ Effective Area / Room Area。**Room Area 用哪個值會影響合規判定**——尤其在 12.5% 邊界 case 上，1 m² 房間面積差距可能跨越合規門檻。

**AI 應對策略**：
1. 報告採光比時，**同時揭露兩個面積值**：「依工具回的『面積』為 X m²，採光比 Y%。若改用『部屋調整値』Z m²，採光比 W%」
2. **不替使用者選**——哪個是合規檢討基準由法務 / 業主 / 設計師決定
3. 若工具回應只有「面積」，主動建議呼叫 `get_element_info` 確認該房間是否有「面積 部屋 調整値」差異

詳見 `domain/lessons.md` L-029、`tool-capability-boundary.md` L-Region。
