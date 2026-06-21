---
name: daylight-area-check
description: 執行建築物居室採光面積檢討流程（依據建築技術規則建築設計施工編第41條）。
metadata:
  version: "1.0"
  updated: "2026-04-05"
  created: "2026-02-24"
  contributors:
    - "DAVID\\david"
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - building-compliance
  tags: [採光, 法規, 居室, 開口, 檢討]
---

# 居室採光面積檢討流程

## 前置條件
- Revit 模型已開啟
- 房間（Rooms）已正確建立並命名（需能識別用途，如：臥室、起居室、教室）
- 窗戶（Windows）或帷幕牆（Curtain Walls）已建立
- 樓板（Floors）已建立（用於計算樓地板面積）

## 執行步驟

### 步驟 1：研讀法規標準 (Analyze Regulation - Article 41)

依據 **建築技術規則建築設計施工編 第 41 條**：

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

## 範例對話

**用戶**：幫我檢查 2F 的採光是否符合法規

**AI**：好的，我將執行居室採光面積檢討流程：
1.  篩選 2F 的居室空間...
2.  計算各房間有效開口面積（扣除台度 75cm 以下部分）...
3.  比對法規標準 (1/8 或 1/5)...

（執行後）

**AI**：檢查完成。
-   **合格**：3 間
-   **不合格**：1 間 (2F-臥室B，採光比 10% < 12.5%)
-   已將不合格房間標示為紅色。

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
