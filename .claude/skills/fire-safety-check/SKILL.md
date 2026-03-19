---
name: fire-safety-check
description: "消防安全檢討：防火時效視覺化、走廊防火分析、外牆開口距離檢討（第 45 條、第 110 條）。觸發條件：使用者提到防火、耐燃、fire rating、防火時效、走廊、corridor、逃生、外牆開口、鄰地距離、Article 45、Article 110、防火區劃。工具：override_element_graphics、check_exterior_wall_openings、query_elements_with_filter。"
---

# 消防安全檢討

## Lessons Reference
- **L-001**：走廊識別必須多語言容錯（走廊/Corridor/廊道/通道/廊下）。詳見 `domain/lessons.md`。

## Sub-Workflows

### 1. 防火時效視覺化

依防火時效參數將牆體上色標記：
1. `get_category_fields` 查詢牆體 → 找到防火時效參數名稱
2. `get_field_values` → 列出專案中所有時效等級
3. `query_elements_with_filter` → 依時效等級篩選牆體
4. `override_element_graphics` → 套用對應顏色

| 時效 | 顏色 |
|------|------|
| 1hr | 綠色 |
| 2hr | 黃色 |
| 3hr | 紅色 |
| 未標註 | 灰色 |

### 2. 走廊防火分析

分析走廊寬度與逃生路線：
1. 篩選名稱含走廊關鍵字的房間：`走廊`、`Corridor`、`廊道`、`通道`、`廊下`（日文）
2. 檢查淨寬是否符合最低標準
3. 驗證防火區劃邊界
4. 在平面視圖上色標示結果

**語言容錯**：查詢時必須同時搜尋中文/英文/日文關鍵字（依 Lesson L-001）。

### 3. 外牆開口檢討（第 45 條 + 第 110 條）

`check_exterior_wall_openings` 自動執行：
- 讀取 PropertyLine（地界線）幾何
- 計算每個牆面開口到地界線的距離
- **第 45 條**：開口距境界線 ≥ 1.0m，同基地建築間 ≥ 2.0m
- **第 110 條**：依距離判定防火間隔時效要求
- 上色標示：紅色=違規、橘色=警告、綠色=合格

## Reference

詳見 `domain/fire-rating-check.md`、`domain/corridor-analysis-protocol.md`、`domain/exterior-wall-opening-check.md`。
