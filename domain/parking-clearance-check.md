---
name: parking-clearance-check
description: "Check if the clearance of parking spaces meets the requirement based on **parking type** (建技規 §62 + 土地使用分區管制). Different..."
metadata:
  version: "1.0"
  updated: "2026-04-16"
  created: "2026-02-10"
  contributors:
    - "HunKue"
    - "shuotao"
  references: []  # TODO: 月小聚補法規條號或外部依據
  related: []  # TODO: 月小聚補相關 domain（檔名）
  referenced_by:
    - building-compliance
    - parking-check
  tags: []  # TODO: 月小聚補分類關鍵字
---

# Domain Analysis: Parking Clearance Check (B1F)

## 法規依據

| 條號 | 內容 |
|:-----|:-----|
| **§60** | 停車位尺寸：寬 2.5m × 長 5.5m（角度 ≤ 30° 時長 6m） |
| **§61** | 車道寬度：單車道 ≥ 3.5m、雙車道 ≥ 5.5m |
| **§62** | 停車空間樓層淨高 ≥ 2.1m（210cm） |
| 無障礙設施設計規範 | 無障礙車位尺寸 3.5m × 5.5m（非建技規，另定規範） |

## 1. Goal Description
Check if the clearance of parking spaces meets the requirement based on **parking type** (建技規 §62 + 土地使用分區管制). Different parking types have different minimum clearance requirements.

### 1.1 車位種類對應淨高標準

> **來源**：Issue #31 (yunchen-kt) — 車位淨高檢查對應不同車位種類決定數值

| 車位種類 | 最低淨高 (cm) | 法規/依據 | Revit 識別方式 |
|:---------|:-------------|:---------|:-------------|
| 一般停車位 | 210 | 建技規 §62 | `停車位類型` = "法定" 或預設 |
| 無障礙停車位 | 210 | 建技規 §62 + 無障礙設施設計規範 | `停車位類型` = "無障礙" |
| 裝卸車位 | 270 | 土地使用分區管制（依各縣市） | `停車位類型` = "裝卸" |
| 大客車停車位 | 380 | 土地使用分區管制（依各縣市） | `停車位類型` = "大客車" |
| 機車停車位 | 190 | 各縣市自治條例（參考值） | `停車位類型` = "機車" |

> **注意**：裝卸車位與大客車停車位的淨高要求依各縣市土地使用分區管制而異，上表為常見標準值。實際專案應依當地法規確認。AI 執行時應允許使用者透過參數覆寫預設值。

### 1.2 驗證邏輯
- 讀取車位的 `停車位類型` 參數值
- 根據上表對應最低淨高標準（若參數為空，預設為一般停車位 210cm）
- 允許使用者傳入 `zoningRules` 參數覆寫特殊車位的淨高要求
- 不合格者以紅色標示，合格者以綠色標示

## 2. Technical Approach
1. **Identify Target Elements**: 
   - Category: `Parking` (OST_Parking)
   - Level: Check only elements on the B1F level (or active view level).
   
2. **Calculate Clearance**:
   - Get the bounding box of each parking element.
   - Use `ReferenceIntersector` to raycast upwards from the center of the parking space.
   - Find the distance to the nearest obstruction (Ceiling, Beam, Duct, Pipe, Floor above).
   - Clearance = Distance from floor to obstruction.

3. **Validation Logic**:
   - 根據車位種類查表取得對應最低淨高（見 §1.1）
   - `Clearance > 最低淨高` -> Pass (Color: Green or Default)
   - `Clearance <= 最低淨高` -> Fail (Color: Red)
   - 若有 `zoningRules` 參數，優先使用使用者指定的淨高標準

4. **Visualization**:
   - Use `OverrideGraphicSettings` to set the projection surface pattern color to Red for failing elements.
   
## 3. Implementation Plan
- [ ] Create `MCP-Server/src/tools/parking_clearance.ts` (or similar JS script).
- [ ] Implement raycasting logic using Revit API.
- [ ] Implement graphic override logic.
