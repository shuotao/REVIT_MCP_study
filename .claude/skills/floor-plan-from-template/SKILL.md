---
name: floor-plan-from-template
description: "以指定的 FloorPlan 視圖為範本，在多個 Level 上批次建立樓層平面視圖（複製 ViewFamilyType、View Template、Phase、Phase Filter），並可選擇性把新 view 的 CropBox 對齊到指定 Detail Group（例如圖框）。支援 Viewport 選取自動解析為底層 view。適用於補齊缺漏的正式建築圖系列（A-X層平面圖）、統一系列裁剪範圍。觸發條件：使用者提到批次建立樓層平面、補齊平面圖、補漏平面圖、依範本建視圖、create floor plans、A-系列缺漏、樓層平面圖缺漏、align cropbox 到圖框、統一裁剪範圍、對齊圖框、viewport 對齊、這個視圖對齊 cropbox、選中的視圖對齊。工具：get_all_views、get_all_levels、get_element_info、get_active_view、get_selected_elements、create_floor_plans_from_template、align_view_cropbox_to_element、query_elements_with_filter、get_viewport_map。"
---

# 依範本批次建立樓層平面圖（並對齊 CropBox）

## Available Tools

| 工具 | 用途 |
|------|------|
| `get_all_views` | 列出所有 FloorPlan，辨識現有系列與缺漏 |
| `get_all_levels` | 確認目標 Level 名稱與 ElementId |
| `get_element_info` | 查範本 view 的 Type / View Template / Phase / Scale；也可從 Viewport 元素取底層 View Name |
| `get_active_view` | 取得目前開啟的視圖；若為 DrawingSheet 要特別處理 |
| `create_floor_plans_from_template` | 在多個 Level 上批次建立 FloorPlan，複製範本設定 |
| `get_selected_elements` | 讓使用者在 Revit 手動選取參考元素（圖框 Detail Group 或 Viewport） |
| `query_elements_with_filter` | 依名稱搜尋圖框（Category 常為 `IOSAttachedDetailGroups`；注意繁簡體差異） |
| `align_view_cropbox_to_element` | 將指定 view 的 CropBox 對齊到目標元素的 BoundingBox |
| `get_viewport_map` | 取得圖紙與 viewport 的對應；Viewport → 底層 view 反查 |

## Workflow 1：補齊缺漏的樓層平面圖

### 步驟 1：盤點現有系列

`get_all_views viewType=FloorPlan` → 過濾目標系列（例：`A-` 前綴 + `層平面圖`），依樓層排序。整理表格並**明確列出缺漏**（哪些 Level 沒有主版、哪些只有變體/OLD）。

### 步驟 2：使用者確認範圍

分類展示（主版 / 變體 / OLD / Dependent），用 `AskUserQuestion` 詢問：
- 要補建哪些樓層？
- 命名慣例？（中文數字「十一」、阿拉伯數字「11」、樓層 code「11FL」等）
- 要用哪個現有 view 當範本？

### 步驟 3：確認範本與樓層

- `get_element_info elementId={範本 ViewId}` → 確認 Type（例 `樓板平面圖`）、View Template（例 `建築平面`）、Phase / Phase Filter、Scale
- `get_all_levels` → 確認目標 Level 名稱完全一致（大小寫敏感）

### 步驟 4：批次建立

```json
create_floor_plans_from_template({
  "templateViewId": 3510637,
  "creations": [
    { "levelName": "11FL", "newName": "A-十一層平面圖" },
    { "levelName": "12FL", "newName": "A-十二層平面圖" }
  ],
  "applyViewTemplate": true
})
```

驗證回傳：`CreatedCount` / `SkippedCount`，查 `Skipped[].Reason`（Level 不存在 / 名稱衝突）。記下新 view 的 `ElementId` 清單供下一步使用。

## Workflow 2：將系列平面圖的 CropBox 對齊到圖框

### 步驟 1：識別圖框元素

**選項 A（推薦）**：請使用者在 Revit 選取圖框 → `get_selected_elements` 抓 ID
**選項 B**：名稱搜尋（注意繁簡體差異，例 `图框` vs `圖框`）：
```json
query_elements_with_filter({
  "category": "IOSAttachedDetailGroups",
  "filters": [{ "field": "Name", "operator": "contains", "value": "图框" }]
})
```

### 步驟 2：批次對齊

**平行呼叫**：同一 message 內對每個 view 呼叫 `align_view_cropbox_to_element`
```json
{ "viewId": <viewId>, "elementId": <圖框ID>, "padding_mm": 0 }
```

可視需要加 `padding_mm`（向外擴邊距）。

### 步驟 3：驗證

比對回傳的 `NewCropBox_mm`：
- 大多數 view 的 Min/Max 應一致（寬高 = 圖框尺寸）
- **例外**：若某 view 的 CropBox Transform Origin 不同（例 B1F 常有 -400k mm 的 X 偏移），數值會不同但**世界座標上仍對齊同一圖框**。切到該 view 目視確認即可。

## Workflow 3：當使用者說「這個/選中的視圖也要對齊」

使用者可能處於以下三種狀態之一：

### 情境 A — 選中 Viewport（在圖紙上點選視埠）
`get_selected_elements` 回傳 `Category: "Viewports"` → 需要**解析為底層 view**：
1. 對 Viewport ElementId 呼叫 `get_element_info`
2. 從回傳的 Parameters 找 `View Name`（例「1FL」）
3. 用 `get_all_views` 比對 Name（**不要猜 Id 差值**；`view.Id = viewport.Id - 20` 這類模式不可靠）
4. 對找到的 view ElementId 呼叫 `align_view_cropbox_to_element`

### 情境 B — 選中 View（在 Project Browser 點 view）
`get_selected_elements` 回傳 `Category: "Views"` → 直接用該 ElementId

### 情境 C — 沒選中元素，只開了某個 view
先試 `get_active_view`：
- 若是 FloorPlan / Section 等可 align 的 view → 用 active view Id
- 若是 **DrawingSheet**（圖紙）→ 停下來問使用者（圖紙本身沒有實際意義的 CropBox）

## 合併 Workflow（建立 + 對齊一站式）

1. Workflow 1 建完新 view，取得 ElementId 清單
2. Workflow 2 步驟 1 識別圖框
3. 對新 view 清單批次呼叫 `align_view_cropbox_to_element`

## 批次「全部對齊」的風險過濾

當使用者要求「所有 X 系列 FloorPlan 都對齊圖框」時，**要警示以下幾類 view 可能結果異常**，建議先用 `AskUserQuestion` 確認是否要排除：

| 視圖類型 | 風險 |
|---------|------|
| **1:50 詳圖**（樓梯詳圖、室外通路詳圖等） | 原 CropBox 很小（<10000 mm），對齊後會被放大 20 倍，失去「詳圖」意義 |
| **1:2000 位置圖** / 1:500 **套繪/現況/畸零地** | 原 CropBox 涵蓋整個基地，對齊後會被縮小到只剩建築範圍 |
| **CropBox 有旋轉的 view**（例如套繪圖、開放空間計畫） | 結果的 Width/Height 會與標準互換（如 42759×81390 vs 76986×50258） |
| **Dependent view** | CropBox 被父視圖控制，align 可能無效或被覆蓋 |

執行前建議先呼叫 `get_all_views` 檢視 Scale 欄位，把 Scale ≤ 100（詳圖）或 ≥ 500（大範圍）的 view 列出來請使用者勾選排除。

## Notes

- **範本選擇**：挑與缺漏樓層同系列的相鄰層 view（例要補 11FL，選 A-十層平面圖最一致）
- **命名慣例**：確認既有系列的命名後沿用；中文數字（一、二、…、十、十一）是傳統建築圖慣例
- **Phase / Phase Filter**：tool 會從範本 view 主動複製（不依賴 View Template 是否含這些設定）
- **Name 衝突**：已存在同名 view 會進 `Skipped`，不影響其他 creation
- **變體與 OLD**：使用者說「所有 X 系列」通常指主版；變體（無障礙、開放空間獎勵、OLD/ARCHIVE、Dependent）應個別用 `AskUserQuestion` 確認
- **繁簡體陷阱**：Detail Group 名稱可能是 `图框`（簡體）而非 `圖框`（繁體），搜尋無果時改用 `get_selected_elements` 讓使用者直接選
- **CropBox Transform**：不同 view 的 `Transform.Origin` 可能不同；`align_view_cropbox_to_element` 已用 `Transform.Inverse.OfPoint()` 正確映射世界座標（結果的數值可能不同但世界區域對齊）
- **Viewport ≠ View**：使用者在圖紙上選 viewport 時，回傳的 ElementId 是 viewport，不是底層 view。一定要透過 `get_element_info` 讀 `View Name` 再查 `get_all_views` 對應 ID
- **圖框 Detail Group 的 OwnerView**：Detail Group 雖然是 view-specific，但其世界座標 BoundingBox 可以被 `get_BoundingBox(otherView)` 正確回傳，因此同一個圖框 ID 可以被不同 view 引用對齊（實測跨 B2F~14FL 皆可）
- **每次 align 是獨立 Transaction**：單一 view Ctrl+Z 可還原該 view 的 CropBox；批次 align 要逐個還原

## Reference

- `domain/dependent-view-crop-workflow.md`（CropBox Transform 機制與網格裁剪）
- `domain/session-context-guard.md`（確保操作的視圖與樓層正確）
