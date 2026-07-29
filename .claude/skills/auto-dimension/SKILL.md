---
name: auto-dimension
description: "自動標註尺寸：使用 Ray-Casting / BoundingBox / Wall-Batch 方法，在平面視圖中自動建立房間、走廊、MEP、整批牆段的尺寸標註。觸發條件：使用者提到標註、尺寸、dimension、annotation、淨寬、淨高、measurement、自動標註、批次標註、自動標尺寸、外牆總尺寸、串接尺寸。工具：create_dimension_by_ray、create_dimension_by_bounding_box、auto_dimension_walls、get_room_info。"
---

# 自動標註尺寸

## Lessons Reference
- **L-002**：標註必須匹配正確的視圖 ID，嚴禁在 3D 視圖建立平面標註。位置線用 BoundingBox 中心 `(max+min)/2`。詳見 `domain/lessons.md`。

## Method Selection

| 場景 | 方法 | 工具 |
|------|------|------|
| 一般矩形房間 | Ray-Casting | `create_dimension_by_ray` |
| L 形或不規則房間 | BoundingBox | `create_dimension_by_bounding_box` |
| MEP 設備淨空檢查 | Ray-Casting | `create_dimension_by_ray` |
| **批次牆段標註（無 Room、剛蓋完牆）** | **Wall-Batch** | **`auto_dimension_walls`** |

## Wall-Batch Workflow（剛蓋完一批牆要標尺寸）

`auto_dimension_walls` 不依賴 Room，三模式擇一：

| mode | 用途 | 輸出條數 |
|------|------|---------|
| `overall_bbox`（預設）| 外圍兩條總長串：top 邊沿 X、right 邊沿 Y | 2 |
| `chained` | 同列／同排共線牆串成 string dimension（每列一條） | N（依 row/col 數） |
| `per_wall` | 每道牆獨立一個長度標註 | 每牆一條 |

### 參數
- `viewId`（必填）：必須是 `ViewPlan`
- `wallIds`（選填）：未提供則自動抓 view 範圍內所有牆
- `mode`（預設 `overall_bbox`）
- `offsetMm`（預設 1500）

### 適用情境
- sketch-to-revit 蓋完牆後自動補總尺寸（`overall_bbox`）
- 給審核版圖紙的詳細串接尺寸（`chained`）
- debug 個別牆長度（`per_wall`）

### 行為
- 標註直接綁牆面 reference（exterior side face / end-cap face），牆移動時 **dimension 會跟著走**，牆刪掉 dimension **自動消失**
- per_wall：抓兩端 end-cap face；chained / overall_bbox：與軸平行的牆抓 end-cap、垂直於軸的牆抓 side face
- 牆面有開洞時取距 endpoint 最近的 PlanarFace（FirstOrDefault 行為）

### 限制
- 只支援直線牆（曲線牆會被 skip）
- 視圖必須是 `ViewPlan`（非 3D / Drafting / Section）
- 拿不到 face reference 的牆（geometry 異常）會被跳過，不會中止整批

## Ray-Casting Workflow

1. 取得房間中心：`get_room_info` → 提取 `Location` 座標
2. 沿 X+/X- 方向發射射線 → 偵測牆面 → 建立 X 軸標註
3. 沿 Y+/Y- 方向發射射線 → 偵測牆面 → 建立 Y 軸標註

### 關鍵參數
- `viewId`：目標平面視圖（**必須是平面視圖，嚴禁 3D 視圖**）
- `origin`：房間中心點（從 `get_room_info` 取得）
- `direction` / `counterDirection`：射線向量

## BoundingBox Workflow (Fallback)

1. 透過 `create_dimension_by_bounding_box` 取得房間包圍框
2. 指定軸向（`X` 或 `Y`）和偏移量（預設 500mm）
3. 系統在最小/最大邊界建立 DetailCurves 並標註

## Key Rules

- **嚴禁**在 3D 視圖中建立 2D 標註 — 必須先確認 `ActiveView` 類型
- 標註線位置 = `(BoundingBox.Max + BoundingBox.Min) / 2` 中心軌跡
- 如果柱子擋住射線，標註到柱子（這對 MEP 淨空檢查是正確的）
- 支援批次套用至整個樓層

## Reference

詳見 `domain/auto-dimension-workflow.md`。
