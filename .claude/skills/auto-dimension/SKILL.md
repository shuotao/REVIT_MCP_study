---
name: auto-dimension
description: "自動標註尺寸：使用 Ray-Casting 或 BoundingBox 方法，在平面視圖中自動建立房間、走廊、MEP 設備的尺寸標註。觸發條件：使用者提到標註、尺寸、dimension、annotation、淨寬、淨高、measurement、自動標註、批次標註。工具：create_dimension_by_ray、create_dimension_by_bounding_box、get_room_info。"
---

# 自動標註尺寸

## Lessons Reference
- **L-002**：標註必須匹配正確的視圖 ID，嚴禁在 3D 視圖建立平面標註。位置線用 BoundingBox 中心 `(max+min)/2`。詳見 `domain/lessons.md`。

## 方法選擇

| 場景 | 方法 | 工具 |
|------|------|------|
| 一般矩形房間 | Ray-Casting | `create_dimension_by_ray` |
| L 形或不規則房間 | BoundingBox | `create_dimension_by_bounding_box` |
| MEP 設備淨空檢查 | Ray-Casting | `create_dimension_by_ray` |

## Ray-Casting 流程

1. 取得房間中心：`get_room_info` → 提取 `Location` 座標
2. 沿 X+/X- 方向發射射線 → 偵測牆面 → 建立 X 軸標註
3. 沿 Y+/Y- 方向發射射線 → 偵測牆面 → 建立 Y 軸標註

### 關鍵參數
- `viewId`：目標平面視圖（**必須是平面視圖，嚴禁 3D 視圖**）
- `origin`：房間中心點（從 `get_room_info` 取得）
- `direction` / `counterDirection`：射線向量

## BoundingBox 流程（備選方案）

1. 透過 `create_dimension_by_bounding_box` 取得房間包圍框
2. 指定軸向（`X` 或 `Y`）和偏移量（預設 500mm）
3. 系統在最小/最大邊界建立 DetailCurves 並標註

## 重要規則

- **嚴禁**在 3D 視圖中建立 2D 標註 — 必須先確認 `ActiveView` 類型
- 標註線位置 = `(BoundingBox.Max + BoundingBox.Min) / 2` 中心軌跡
- 如果柱子擋住射線，標註到柱子（這對 MEP 淨空檢查是正確的）
- 支援批次套用至整個樓層

## 參考

詳見 `domain/auto-dimension-workflow.md`。
